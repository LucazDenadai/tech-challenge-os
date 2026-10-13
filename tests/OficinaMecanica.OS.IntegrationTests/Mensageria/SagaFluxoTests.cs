using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OficinaMecanica.OS.IntegrationTests.Api;
using OficinaMecanica.OS.IntegrationTests.Fixtures;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace OficinaMecanica.OS.IntegrationTests.Mensageria;

// Fluxo normal da Saga pelo broker real (CARD-40a). O teste faz o papel de Billing e Operações: publica os eventos
// nas exchanges deles e lê, em filas próprias, os comandos que o OS publica pela outbox.
[Collection(PostgresCollection.Nome)]
public class SagaFluxoTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly string[] _comandos =
    [
        "saga-os.diagnosis-requested.v1", "saga-os.quote-requested.v1", "saga-os.inventory-reservation-requested.v1",
        "saga-os.payment-creation-requested.v1", "saga-os.execution-start-requested.v1"
    ];

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:3.13-alpine")
        .WithUsername("os-teste")
        .WithPassword("os-teste-senha")
        .Build();

    private string _banco = null!;
    private Uri _uri = null!;
    private OsApiFactory _factory = null!;
    private IConnection _conexao = null!;
    private IChannel _canal = null!;

    public async Task InitializeAsync()
    {
        await _rabbit.StartAsync();
        _uri = new Uri(_rabbit.GetConnectionString());
        _banco = postgres.NovoBancoVazio();

        _conexao = await new ConnectionFactory { Uri = _uri, UserName = "os-teste", Password = "os-teste-senha" }.CreateConnectionAsync();
        _canal = await _conexao.CreateChannelAsync();
        foreach (var canal in _comandos)
        {
            // Filas do "participante" do teste, ligadas antes de o OS publicar qualquer comando.
            await _canal.ExchangeDeclareAsync(canal, ExchangeType.Fanout, durable: true, autoDelete: false);
            await _canal.QueueDeclareAsync(Fila(canal), durable: false, exclusive: false, autoDelete: false);
            await _canal.QueueBindAsync(Fila(canal), canal, string.Empty);
        }

        await SubirOsAsync();
    }

    public async Task DisposeAsync()
    {
        await _canal.DisposeAsync();
        await _conexao.DisposeAsync();
        await _factory.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    [Fact]
    public async Task FluxoNormal_DaAberturaAConclusao_ComReinicioNoMeio()
    {
        var saga = await CenarioSaga.AbrirOsAsync(_factory);
        var peca = Guid.NewGuid();
        var execucao = Guid.NewGuid();
        var quote = Guid.NewGuid();
        var reserva = Guid.NewGuid();
        var pagamento = Guid.NewGuid();

        // Abertura → DiagnosisRequested, primeiro comando, sem causationId.
        var diagnostico = await ReceberAsync("saga-os.diagnosis-requested.v1");
        Assert.Equal(saga.CorrelationId.ToString(), diagnostico["correlationId"]!.GetValue<string>());
        Assert.Equal($"os:{saga.OsId}:diagnosis", diagnostico["idempotencyKey"]!.GetValue<string>());
        Assert.Null(diagnostico["causationId"]);

        // Operações diagnostica → OS pede orçamento com os itens e preços do snapshot.
        var concluido = await PublicarAsync("saga-os.diagnosis-completed.v1", "DiagnosisCompleted.v1", "operacoes", saga, new JsonObject
        {
            ["executionId"] = execucao.ToString(), ["priceSnapshotAtUtc"] = "2026-10-13T12:00:00Z", ["currency"] = "BRL",
            ["items"] = new JsonArray(
                new JsonObject { ["itemId"] = peca.ToString(), ["type"] = "Peca", ["description"] = "Filtro de óleo", ["quantity"] = 2, ["unitPrice"] = 45.90m },
                new JsonObject { ["itemId"] = Guid.NewGuid().ToString(), ["type"] = "Servico", ["description"] = "Troca de óleo", ["quantity"] = 1, ["unitPrice"] = 80m })
        });
        var pedidoOrcamento = await ReceberAsync("saga-os.quote-requested.v1");
        Assert.Equal(concluido.ToString(), pedidoOrcamento["causationId"]!.GetValue<string>());
        Assert.Equal(saga.ClienteId.ToString(), pedidoOrcamento["clienteId"]!.GetValue<string>());
        Assert.Equal(2, pedidoOrcamento["items"]!.AsArray().Count);

        // Billing calcula e o cliente aprova → OS pede a reserva só das peças.
        await PublicarAsync("saga-os.quote-ready.v1", "QuoteReady.v1", "billing", saga, new JsonObject
        {
            ["quoteId"] = quote.ToString(), ["quoteVersion"] = 1, ["amount"] = 171.80m, ["currency"] = "BRL", ["expiresAtUtc"] = "2026-10-14T12:00:00Z"
        });
        await AguardarEstadoAsync(saga, "AwaitingApproval");
        var aprovacao = new JsonObject { ["quoteId"] = quote.ToString(), ["quoteVersion"] = 1, ["decidedAtUtc"] = "2026-10-13T12:05:00Z" };
        var aprovado = await PublicarAsync("saga-os.quote-approved.v1", "QuoteApproved.v1", "billing", saga, aprovacao);
        var pedidoReserva = await ReceberAsync("saga-os.inventory-reservation-requested.v1");
        var item = Assert.Single(pedidoReserva["items"]!.AsArray())!;
        Assert.Equal(peca.ToString(), item["pecaId"]!.GetValue<string>());
        Assert.Equal(2, item["quantity"]!.GetValue<int>());

        // Reentrega da mesma mensagem e cópia com outro messageId: nenhum comando novo.
        await PublicarAsync("saga-os.quote-approved.v1", "QuoteApproved.v1", "billing", saga, aprovacao, aprovado);
        await PublicarAsync("saga-os.quote-approved.v1", "QuoteApproved.v1", "billing", saga, aprovacao);

        // O OS reinicia: a Saga continua do estado gravado.
        await _factory.DisposeAsync();
        await SubirOsAsync();
        Assert.Equal(0u, await ContarAsync("saga-os.inventory-reservation-requested.v1"));

        // Operações reserva → OS pede o pagamento com valor aprovado e e-mail do cliente.
        await PublicarAsync("saga-os.inventory-reserved.v1", "InventoryReserved.v1", "operacoes", saga, new JsonObject
        {
            ["reservationId"] = reserva.ToString(), ["items"] = new JsonArray(new JsonObject { ["pecaId"] = peca.ToString(), ["quantity"] = 2 })
        });
        var pedidoPagamento = await ReceberAsync("saga-os.payment-creation-requested.v1");
        Assert.Equal(171.80m, pedidoPagamento["amount"]!.GetValue<decimal>());
        Assert.Equal(quote.ToString(), pedidoPagamento["quoteId"]!.GetValue<string>());
        Assert.Equal("cliente1@cliente.example", pedidoPagamento["payerEmail"]!.GetValue<string>());
        Assert.Equal($"os:{saga.OsId}:payment:1", pedidoPagamento["idempotencyKey"]!.GetValue<string>());

        // Billing confirma → OS pede o início da execução reservada.
        await PublicarAsync("saga-os.payment-pending.v1", "PaymentPending.v1", "billing", saga, new JsonObject { ["paymentId"] = pagamento.ToString() });
        await PublicarAsync("saga-os.payment-approved.v1", "PaymentApproved.v1", "billing", saga, new JsonObject
        {
            ["paymentId"] = pagamento.ToString(), ["amount"] = 171.80m, ["currency"] = "BRL", ["approvedAtUtc"] = "2026-10-13T12:10:00Z"
        });
        var inicio = await ReceberAsync("saga-os.execution-start-requested.v1");
        Assert.Equal(execucao.ToString(), inicio["executionId"]!.GetValue<string>());
        Assert.Equal(reserva.ToString(), inicio["reservationId"]!.GetValue<string>());

        // Operações executa e conclui → Saga Completed, OS Finalizada.
        await PublicarAsync("saga-os.execution-started.v1", "ExecutionStarted.v1", "operacoes", saga, new JsonObject
        {
            ["executionId"] = execucao.ToString(), ["startedAtUtc"] = "2026-10-13T12:15:00Z"
        });
        await PublicarAsync("saga-os.execution-completed.v1", "ExecutionCompleted.v1", "operacoes", saga, new JsonObject
        {
            ["executionId"] = execucao.ToString(), ["completedAtUtc"] = "2026-10-13T13:00:00Z",
            ["consumedItems"] = new JsonArray(new JsonObject { ["pecaId"] = peca.ToString(), ["quantity"] = 2 })
        });
        var final = await AguardarEstadoAsync(saga, "Completed");
        Assert.True(final.GetProperty("terminada").GetBoolean());

        var http = await _factory.ClienteFuncionarioAsync(CenarioSaga.Atendente);
        var status = await http.GetFromJsonAsync<JsonElement>($"/os/ordens-servico/{saga.OsId}/status");
        Assert.Equal("Finalizada", status.GetProperty("status").GetString());
        // Nenhum comando duplicado ficou pendente em nenhuma fila.
        foreach (var canal in _comandos)
            Assert.Equal(0u, await ContarAsync(canal));
    }

    [Fact]
    public async Task OrcamentoRecusado_CancelaSemPedirReservaNemPagamento()
    {
        var saga = await CenarioSaga.AbrirOsAsync(_factory);
        var quote = Guid.NewGuid();
        await ReceberAsync("saga-os.diagnosis-requested.v1");

        await PublicarAsync("saga-os.diagnosis-completed.v1", "DiagnosisCompleted.v1", "operacoes", saga, new JsonObject
        {
            ["executionId"] = Guid.NewGuid().ToString(), ["priceSnapshotAtUtc"] = "2026-10-13T12:00:00Z", ["currency"] = "BRL",
            ["items"] = new JsonArray(new JsonObject
            {
                ["itemId"] = Guid.NewGuid().ToString(), ["type"] = "Peca", ["description"] = "Pastilha", ["quantity"] = 1, ["unitPrice"] = 120m
            })
        });
        await ReceberAsync("saga-os.quote-requested.v1");
        await PublicarAsync("saga-os.quote-ready.v1", "QuoteReady.v1", "billing", saga, new JsonObject
        {
            ["quoteId"] = quote.ToString(), ["quoteVersion"] = 1, ["amount"] = 120m, ["currency"] = "BRL", ["expiresAtUtc"] = "2026-10-14T12:00:00Z"
        });
        await AguardarEstadoAsync(saga, "AwaitingApproval");
        await PublicarAsync("saga-os.quote-declined.v1", "QuoteDeclined.v1", "billing", saga, new JsonObject
        {
            ["quoteId"] = quote.ToString(), ["quoteVersion"] = 1, ["decidedAtUtc"] = "2026-10-13T12:05:00Z"
        });

        var final = await AguardarEstadoAsync(saga, "Cancelled");
        Assert.Equal("Orçamento recusado pelo cliente.", final.GetProperty("motivo").GetString());
        await Task.Delay(1000);
        Assert.Equal(0u, await ContarAsync("saga-os.inventory-reservation-requested.v1"));
        Assert.Equal(0u, await ContarAsync("saga-os.payment-creation-requested.v1"));
    }

    private async Task SubirOsAsync()
    {
        _factory = new OsApiFactory(_banco, new Dictionary<string, string?>
        {
            ["RabbitMq:Enabled"] = "true",
            ["RabbitMq:Host"] = _uri.Host,
            ["RabbitMq:Port"] = _uri.Port.ToString(),
            ["RabbitMq:Username"] = "os-teste",
            ["RabbitMq:Password"] = "os-teste-senha",
            ["RabbitMq:IntervaloOutboxMs"] = "100"
        });
        var http = _factory.CreateClient();
        await Aguardar(async () => (await http.GetAsync("/os/ready")).IsSuccessStatusCode);
    }

    private static string Fila(string canal) => $"teste.{canal}";

    // Publica um evento de participante para a Saga; devolve o messageId usado.
    private async Task<Guid> PublicarAsync(string canal, string tipo, string produtor, SagaAberta saga, JsonObject payload, Guid? messageId = null)
    {
        var id = messageId ?? Guid.NewGuid();
        var mensagem = new JsonObject
        {
            ["messageId"] = id.ToString(), ["messageType"] = tipo, ["schemaVersion"] = 1, ["producer"] = produtor,
            ["occurredAtUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["correlationId"] = saga.CorrelationId.ToString(),
            ["causationId"] = Guid.NewGuid().ToString(), ["osId"] = saga.OsId.ToString(), ["filialId"] = saga.FilialId.ToString()
        };
        foreach (var (campo, valor) in payload)
            mensagem[campo] = valor?.DeepClone();

        await _canal.BasicPublishAsync(canal, string.Empty, mandatory: false,
            new BasicProperties { ContentType = "application/json", Persistent = true },
            Encoding.UTF8.GetBytes(mensagem.ToJsonString()));
        return id;
    }

    private async Task<JsonNode> ReceberAsync(string canal)
    {
        BasicGetResult? resultado = null;
        await Aguardar(async () => (resultado = await _canal.BasicGetAsync(Fila(canal), autoAck: true)) is not null);
        return JsonNode.Parse(Encoding.UTF8.GetString(resultado!.Body.Span))!;
    }

    private async Task<uint> ContarAsync(string canal) => (await _canal.QueueDeclarePassiveAsync(Fila(canal))).MessageCount;

    private async Task<JsonElement> AguardarEstadoAsync(SagaAberta saga, string estado)
    {
        var http = await _factory.ClienteFuncionarioAsync(CenarioSaga.Atendente);
        JsonElement corpo = default;
        await Aguardar(async () =>
        {
            corpo = await http.GetFromJsonAsync<JsonElement>($"/os/ordens-servico/{saga.OsId}/saga");
            return corpo.GetProperty("estado").GetString() == estado;
        });
        return corpo;
    }

    private static async Task Aguardar(Func<Task<bool>> condicao)
    {
        var limite = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < limite)
        {
            if (await condicao()) return;
            await Task.Delay(200);
        }
        Assert.Fail("Condição não atendida em 30 s.");
    }
}
