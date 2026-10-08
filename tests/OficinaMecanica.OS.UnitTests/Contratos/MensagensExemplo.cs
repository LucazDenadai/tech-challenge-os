using System.Text;
using System.Text.Json.Nodes;
using OficinaMecanica.OS.Application.Contratos.Saga;

namespace OficinaMecanica.OS.UnitTests.Contratos;

// Mensagens válidas de exemplo, uma por canal consumido pelo OS. Sem dados pessoais.
public static class MensagensExemplo
{
    private const string Instante = "2026-10-07T12:00:00Z";

    public static JsonObject Criar(CanalConsumido canal)
    {
        var mensagem = new JsonObject
        {
            ["messageId"] = Guid.NewGuid().ToString(),
            ["messageType"] = canal.MessageType,
            ["schemaVersion"] = canal.SchemaVersion,
            ["producer"] = canal.Produtor,
            ["occurredAtUtc"] = Instante,
            ["correlationId"] = Guid.NewGuid().ToString(),
            ["causationId"] = Guid.NewGuid().ToString(),
            ["osId"] = Guid.NewGuid().ToString(),
            ["filialId"] = Guid.NewGuid().ToString()
        };

        foreach (var (campo, valor) in Payload(canal.TipoMensagem.Name))
            mensagem[campo] = valor;

        return mensagem;
    }

    public static ReadOnlyMemory<byte> Bytes(JsonNode mensagem) => Encoding.UTF8.GetBytes(mensagem.ToJsonString());

    private static IEnumerable<(string, JsonNode)> Payload(string tipo) => tipo switch
    {
        nameof(DiagnosisCompleted) =>
        [
            ("executionId", Id()), ("priceSnapshotAtUtc", Instante), ("currency", "BRL"),
            ("items", new JsonArray(new JsonObject
            {
                ["itemId"] = Id(), ["type"] = "Peca", ["description"] = "Filtro de óleo", ["quantity"] = 1, ["unitPrice"] = 45.90m
            }))
        ],
        nameof(DiagnosisRejected) or nameof(QuoteRejected) => [("reason", "Filial inválida para o diagnóstico")],
        nameof(InventoryReserved) => [("reservationId", Id()), ("items", Pecas())],
        nameof(InventoryReservationRejected) => [("reason", "Saldo insuficiente na filial"), ("unavailablePecaIds", new JsonArray(Id()))],
        nameof(ExecutionStarted) => [("executionId", Id()), ("startedAtUtc", Instante)],
        nameof(ExecutionStartRejected) => [("reason", "Mecânico indisponível"), ("executionId", Id())],
        nameof(ExecutionCompleted) => [("executionId", Id()), ("completedAtUtc", Instante), ("consumedItems", Pecas())],
        nameof(ExecutionFailed) => [("reason", "Peça danificada na instalação"), ("executionId", Id()), ("consumedItems", Pecas())],
        nameof(InventoryReleased) => [("reservationId", Id()), ("releasedItems", Pecas())],
        nameof(QuoteReady) => [("quoteId", Id()), ("quoteVersion", 1), ("amount", 245.90m), ("currency", "BRL"), ("expiresAtUtc", Instante)],
        nameof(QuoteApproved) or nameof(QuoteDeclined) => [("quoteId", Id()), ("quoteVersion", 1), ("decidedAtUtc", Instante)],
        nameof(QuoteExpired) => [("quoteId", Id()), ("quoteVersion", 1)],
        nameof(PaymentPending) => [("paymentId", Id()), ("providerReference", "mp-123")],
        nameof(PaymentApproved) => [("paymentId", Id()), ("providerReference", "mp-123"), ("amount", 245.90m), ("currency", "BRL"), ("approvedAtUtc", Instante)],
        nameof(PaymentDeclined) or nameof(PaymentOutcomeUnknown) => [("paymentId", Id()), ("reason", "Recusado pelo emissor")],
        nameof(PaymentRefunded) or nameof(PaymentRefundPending) => [("paymentId", Id()), ("providerRefundReference", "mp-refund-1")],
        nameof(PaymentRefundFailed) => [("paymentId", Id()), ("reason", "Estorno negado pelo provedor")],
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Sem exemplo para o tipo.")
    };

    private static JsonNode Id() => Guid.NewGuid().ToString();

    private static JsonArray Pecas() => new(new JsonObject { ["pecaId"] = Id(), ["quantity"] = 2 });
}
