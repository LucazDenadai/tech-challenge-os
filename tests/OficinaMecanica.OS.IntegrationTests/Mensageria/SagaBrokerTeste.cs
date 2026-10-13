using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OficinaMecanica.OS.IntegrationTests.Api;
using OficinaMecanica.OS.IntegrationTests.Fixtures;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace OficinaMecanica.OS.IntegrationTests.Mensageria;

// Base dos testes da Saga pelo broker real. O teste faz o papel de Billing e Operações: publica os eventos nas
// exchanges deles e lê, em filas próprias, os comandos que o OS publica pela outbox.
public abstract class SagaBrokerTeste(PostgresFixture postgres) : IAsyncLifetime
{
    protected static readonly string[] Comandos =
    [
        "saga-os.diagnosis-requested.v1", "saga-os.quote-requested.v1", "saga-os.inventory-reservation-requested.v1",
        "saga-os.payment-creation-requested.v1", "saga-os.execution-start-requested.v1",
        "saga-os.inventory-release-requested.v1", "saga-os.payment-refund-requested.v1", "saga-os.execution-cancel-requested.v1"
    ];

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:3.13-alpine")
        .WithUsername("os-teste")
        .WithPassword("os-teste-senha")
        .Build();

    private string _banco = null!;
    private Uri _uri = null!;
    private IConnection _conexao = null!;
    private IChannel _canal = null!;

    protected OsApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _rabbit.StartAsync();
        _uri = new Uri(_rabbit.GetConnectionString());
        _banco = postgres.NovoBancoVazio();

        _conexao = await new ConnectionFactory { Uri = _uri, UserName = "os-teste", Password = "os-teste-senha" }.CreateConnectionAsync();
        _canal = await _conexao.CreateChannelAsync();
        foreach (var canal in Comandos)
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
        await Factory.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    // Sobe (ou sobe de novo) o OS contra o mesmo banco e broker. Extras sobrescrevem a configuração, como os prazos.
    protected async Task SubirOsAsync(IReadOnlyDictionary<string, string?>? extras = null)
    {
        var configuracao = new Dictionary<string, string?>
        {
            ["RabbitMq:Enabled"] = "true",
            ["RabbitMq:Host"] = _uri.Host,
            ["RabbitMq:Port"] = _uri.Port.ToString(),
            ["RabbitMq:Username"] = "os-teste",
            ["RabbitMq:Password"] = "os-teste-senha",
            ["RabbitMq:IntervaloOutboxMs"] = "100"
        };
        foreach (var (chave, valor) in extras ?? new Dictionary<string, string?>())
            configuracao[chave] = valor;

        Factory = new OsApiFactory(_banco, configuracao);
        var http = Factory.CreateClient();
        await Aguardar(async () => (await http.GetAsync("/os/ready")).IsSuccessStatusCode);
    }

    protected async Task ReiniciarOsAsync(IReadOnlyDictionary<string, string?>? extras = null)
    {
        await Factory.DisposeAsync();
        await SubirOsAsync(extras);
    }

    private static string Fila(string canal) => $"teste.{canal}";

    // Publica um evento de participante para a Saga; devolve o messageId usado.
    protected async Task<Guid> PublicarAsync(string canal, string tipo, string produtor, SagaAberta saga, JsonObject payload, Guid? messageId = null)
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

    protected async Task<JsonNode> ReceberAsync(string canal)
    {
        BasicGetResult? resultado = null;
        await Aguardar(async () => (resultado = await _canal.BasicGetAsync(Fila(canal), autoAck: true)) is not null);
        return JsonNode.Parse(Encoding.UTF8.GetString(resultado!.Body.Span))!;
    }

    protected async Task<uint> ContarAsync(string canal) => (await _canal.QueueDeclarePassiveAsync(Fila(canal))).MessageCount;

    protected async Task<JsonElement> AguardarEstadoAsync(SagaAberta saga, string estado)
    {
        var http = await Factory.ClienteFuncionarioAsync(CenarioSaga.Atendente);
        JsonElement corpo = default;
        await Aguardar(async () =>
        {
            corpo = await http.GetFromJsonAsync<JsonElement>($"/os/ordens-servico/{saga.OsId}/saga");
            return corpo.GetProperty("estado").GetString() == estado;
        });
        return corpo;
    }

    protected async Task<string> StatusDaOsAsync(SagaAberta saga)
    {
        var http = await Factory.ClienteFuncionarioAsync(CenarioSaga.Atendente);
        return (await http.GetFromJsonAsync<JsonElement>($"/os/ordens-servico/{saga.OsId}/status")).GetProperty("status").GetString()!;
    }

    protected static async Task Aguardar(Func<Task<bool>> condicao)
    {
        var limite = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < limite)
        {
            if (await condicao()) return;
            await Task.Delay(200);
        }
        Assert.Fail("Condição não atendida em 30 s.");
    }

    // ── Eventos dos participantes ──────────────────────────────────────────────

    protected Task<Guid> DiagnosticarAsync(SagaAberta saga, Guid execucao, params (Guid ItemId, string Tipo, int Quantidade, decimal Preco)[] itens)
        => PublicarAsync("saga-os.diagnosis-completed.v1", "DiagnosisCompleted.v1", "operacoes", saga, new JsonObject
        {
            ["executionId"] = execucao.ToString(), ["priceSnapshotAtUtc"] = "2026-10-13T12:00:00Z", ["currency"] = "BRL",
            ["items"] = new JsonArray(itens.Select(i => (JsonNode)new JsonObject
            {
                ["itemId"] = i.ItemId.ToString(), ["type"] = i.Tipo, ["description"] = i.Tipo == "Peca" ? "Filtro de óleo" : "Balanceamento",
                ["quantity"] = i.Quantidade, ["unitPrice"] = i.Preco
            }).ToArray())
        });

    protected Task<Guid> OrcarAsync(SagaAberta saga, Guid quote, decimal valor)
        => PublicarAsync("saga-os.quote-ready.v1", "QuoteReady.v1", "billing", saga, new JsonObject
        {
            ["quoteId"] = quote.ToString(), ["quoteVersion"] = 1, ["amount"] = valor, ["currency"] = "BRL", ["expiresAtUtc"] = "2026-10-14T12:00:00Z"
        });

    protected Task<Guid> AprovarOrcamentoAsync(SagaAberta saga, Guid quote)
        => PublicarAsync("saga-os.quote-approved.v1", "QuoteApproved.v1", "billing", saga, new JsonObject
        {
            ["quoteId"] = quote.ToString(), ["quoteVersion"] = 1, ["decidedAtUtc"] = "2026-10-13T12:05:00Z"
        });

    protected Task<Guid> ReservarAsync(SagaAberta saga, Guid reserva, Guid peca, int quantidade)
        => PublicarAsync("saga-os.inventory-reserved.v1", "InventoryReserved.v1", "operacoes", saga, new JsonObject
        {
            ["reservationId"] = reserva.ToString(), ["items"] = new JsonArray(new JsonObject { ["pecaId"] = peca.ToString(), ["quantity"] = quantidade })
        });

    protected Task<Guid> PagamentoAsync(SagaAberta saga, string tipo, Guid pagamento, decimal valor = 0)
    {
        var payload = new JsonObject { ["paymentId"] = pagamento.ToString() };
        if (tipo == "PaymentApproved")
        {
            payload["amount"] = valor;
            payload["currency"] = "BRL";
            payload["approvedAtUtc"] = "2026-10-13T12:10:00Z";
        }
        if (tipo is "PaymentDeclined" or "PaymentOutcomeUnknown")
            payload["reason"] = tipo == "PaymentDeclined" ? "Pix expirado" : "Timeout no provedor";

        var canal = tipo switch
        {
            "PaymentPending" => "saga-os.payment-pending.v1",
            "PaymentApproved" => "saga-os.payment-approved.v1",
            "PaymentDeclined" => "saga-os.payment-declined.v1",
            "PaymentOutcomeUnknown" => "saga-os.payment-outcome-unknown.v1",
            "PaymentRefunded" => "saga-os.payment-refunded.v1",
            _ => throw new ArgumentOutOfRangeException(nameof(tipo))
        };
        return PublicarAsync(canal, $"{tipo}.v1", "billing", saga, payload);
    }

    protected Task<Guid> ExecucaoAsync(SagaAberta saga, string tipo, Guid execucao)
    {
        var payload = new JsonObject { ["executionId"] = execucao.ToString() };
        var canal = tipo switch
        {
            "ExecutionStarted" => "saga-os.execution-started.v1",
            "ExecutionFailed" => "saga-os.execution-failed.v1",
            "ExecutionCompleted" => "saga-os.execution-completed.v1",
            "ExecutionCancelled" => "saga-os.execution-cancelled.v1",
            "ExecutionCancelRejected" => "saga-os.execution-cancel-rejected.v1",
            _ => throw new ArgumentOutOfRangeException(nameof(tipo))
        };
        if (tipo == "ExecutionStarted") payload["startedAtUtc"] = "2026-10-13T12:15:00Z";
        if (tipo == "ExecutionCompleted") { payload["completedAtUtc"] = "2026-10-13T13:00:00Z"; payload["consumedItems"] = new JsonArray(); }
        if (tipo == "ExecutionFailed") { payload["reason"] = "Peça danificada"; payload["consumedItems"] = new JsonArray(); }
        if (tipo == "ExecutionCancelRejected") payload["reason"] = "Execução no status 'EmReparo' não pode ser cancelada.";
        return PublicarAsync(canal, $"{tipo}.v1", "operacoes", saga, payload);
    }

    protected Task<Guid> LiberadaAsync(SagaAberta saga, Guid reserva)
        => PublicarAsync("saga-os.inventory-released.v1", "InventoryReleased.v1", "operacoes", saga, new JsonObject
        {
            ["reservationId"] = reserva.ToString(), ["releasedItems"] = new JsonArray()
        });
}
