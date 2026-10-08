using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using OficinaMecanica.OS.IntegrationTests.Api;
using OficinaMecanica.OS.IntegrationTests.Fixtures;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace OficinaMecanica.OS.IntegrationTests.Mensageria;

// Consumo real pelo RabbitMQ: inbox, deduplicação por messageId e DLQ com motivo (CARD-37b).
[Collection(PostgresCollection.Nome)]
public class ConsumidorSagaTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Exchange = "saga-os.payment-approved.v1";
    private const string Fila = "os.saga-os.payment-approved.v1";
    private const string Dlq = "os.saga-os.payment-approved.v1.dlq";

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:3.13-alpine")
        .WithUsername("os-teste")
        .WithPassword("os-teste-senha")
        .Build();

    private OsApiFactory _factory = null!;
    private IConnection _conexao = null!;
    private IChannel _canal = null!;

    public async Task InitializeAsync()
    {
        await _rabbit.StartAsync();
        var uri = new Uri(_rabbit.GetConnectionString());

        _factory = new OsApiFactory(postgres.NovoBancoVazio(), new Dictionary<string, string?>
        {
            ["RabbitMq:Enabled"] = "true",
            ["RabbitMq:Host"] = uri.Host,
            ["RabbitMq:Port"] = uri.Port.ToString(),
            ["RabbitMq:Username"] = "os-teste",
            ["RabbitMq:Password"] = "os-teste-senha"
        });

        // Readiness só fica 200 quando o consumidor declarou as filas e está consumindo.
        var http = _factory.CreateClient();
        await Aguardar(async () => (await http.GetAsync("/os/ready")).StatusCode == HttpStatusCode.OK);

        _conexao = await new ConnectionFactory { Uri = uri, UserName = "os-teste", Password = "os-teste-senha" }.CreateConnectionAsync();
        _canal = await _conexao.CreateChannelAsync();
    }

    public async Task DisposeAsync()
    {
        await _canal.DisposeAsync();
        await _conexao.DisposeAsync();
        await _factory.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    [Fact]
    public async Task MensagemValida_RegistradaNaInboxComCorrelationId()
    {
        var mensagem = PagamentoAprovado();

        await PublicarAsync(mensagem);

        await Aguardar(async () => await ContarInboxAsync() == 1);
        await using var db = PostgresFixture.CriarContexto(_factory.ConnectionString);
        var registro = await db.InboxMensagens.SingleAsync();
        Assert.Equal(Guid.Parse(mensagem["messageId"]!.GetValue<string>()), registro.MessageId);
        Assert.Equal(Guid.Parse(mensagem["correlationId"]!.GetValue<string>()), registro.CorrelationId);
        Assert.Equal("PaymentApproved.v1", registro.MessageType);
        Assert.Equal(Exchange, registro.Canal);
    }

    [Fact]
    public async Task MensagemReentregue_ConfirmadaSemNovoRegistro()
    {
        var mensagem = PagamentoAprovado();

        await PublicarAsync(mensagem);
        await PublicarAsync(mensagem);

        // As duas entregas são confirmadas (fila vazia) e só uma linha existe na inbox.
        await Aguardar(async () => await ContarFilaAsync(Fila) == 0 && await ContarInboxAsync() == 1);
        await Task.Delay(500);
        Assert.Equal(1, await ContarInboxAsync());
        Assert.Equal(0u, await ContarFilaAsync(Dlq));
    }

    [Fact]
    public async Task MensagemForaDoContrato_VaiParaDlqComMotivoESemAlterarEstado()
    {
        var mensagem = PagamentoAprovado();
        mensagem.Remove("amount");

        await PublicarAsync(mensagem);

        await Aguardar(async () => await ContarFilaAsync(Dlq) == 1);
        var naDlq = await _canal.BasicGetAsync(Dlq, autoAck: true);
        var cabecalhos = naDlq!.BasicProperties.Headers!;
        Assert.Contains("Payload fora do contrato", Texto(cabecalhos["x-motivo"]));
        Assert.Equal(Exchange, Texto(cabecalhos["x-canal-origem"]));
        Assert.Equal(mensagem["correlationId"]!.GetValue<string>(), Texto(cabecalhos["x-correlation-id"]));
        Assert.Equal(mensagem.ToJsonString(), Encoding.UTF8.GetString(naDlq.Body.Span));
        Assert.Equal(0, await ContarInboxAsync());
    }

    private static JsonObject PagamentoAprovado() => new()
    {
        ["messageId"] = Guid.NewGuid().ToString(),
        ["messageType"] = "PaymentApproved.v1",
        ["schemaVersion"] = 1,
        ["producer"] = "billing",
        ["occurredAtUtc"] = "2026-10-07T12:00:00Z",
        ["correlationId"] = Guid.NewGuid().ToString(),
        ["causationId"] = Guid.NewGuid().ToString(),
        ["osId"] = Guid.NewGuid().ToString(),
        ["filialId"] = Guid.NewGuid().ToString(),
        ["paymentId"] = Guid.NewGuid().ToString(),
        ["amount"] = 245.90m,
        ["currency"] = "BRL",
        ["approvedAtUtc"] = "2026-10-07T12:01:00Z"
    };

    private async Task PublicarAsync(JsonObject mensagem)
        => await _canal.BasicPublishAsync(Exchange, string.Empty, mandatory: false,
            new BasicProperties { ContentType = "application/json", Persistent = true },
            Encoding.UTF8.GetBytes(mensagem.ToJsonString()));

    private async Task<int> ContarInboxAsync()
    {
        await using var db = PostgresFixture.CriarContexto(_factory.ConnectionString);
        return await db.InboxMensagens.CountAsync();
    }

    private async Task<uint> ContarFilaAsync(string fila) => (await _canal.QueueDeclarePassiveAsync(fila)).MessageCount;

    private static string? Texto(object? valor) => valor is byte[] bytes ? Encoding.UTF8.GetString(bytes) : valor?.ToString();

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
