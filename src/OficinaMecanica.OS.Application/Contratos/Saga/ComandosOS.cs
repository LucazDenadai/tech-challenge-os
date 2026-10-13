namespace OficinaMecanica.OS.Application.Contratos.Saga;

// Comandos publicados pelo OS, orquestrador da Saga (asyncapi-saga-os.yaml, ADR-018).
// Todos usam o CommandEnvelope: idempotencyKey obrigatória.

public sealed record DiagnosisRequested : MensagemSaga
{
    public required Guid VeiculoId { get; init; }
}

public sealed record QuoteRequested : MensagemSaga
{
    public required Guid ClienteId { get; init; }
    public required DateTimeOffset PriceSnapshotAtUtc { get; init; }
    public required string Currency { get; init; }
    public required IReadOnlyList<PricedItem> Items { get; init; }
}

public sealed record InventoryReservationRequested : MensagemSaga
{
    public required IReadOnlyList<PecaQuantity> Items { get; init; }
}

public sealed record PaymentCreationRequested : MensagemSaga
{
    public required Guid QuoteId { get; init; }
    public required int QuoteVersion { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    // Exigido por Billing para o Pix (ADR-019); único dado pessoal do pagador que trafega.
    public string? PayerEmail { get; init; }
}

public sealed record ExecutionStartRequested : MensagemSaga
{
    public required Guid ExecutionId { get; init; }
    // Ausente quando o orçamento não tem peças (serviço sem peça, ADR-020).
    public Guid? ReservationId { get; init; }
}

// Comandos de compensação e cancelamento (CARD-40b, ADR-020).

public sealed record InventoryReleaseRequested : MensagemSaga
{
    public required string Reason { get; init; }
    public required Guid ReservationId { get; init; }
}

public sealed record ExecutionCancelRequested : MensagemSaga
{
    public required string Reason { get; init; }
    public required Guid ExecutionId { get; init; }
}

public sealed record PaymentRefundRequested : MensagemSaga
{
    public required string Reason { get; init; }
    public required Guid PaymentId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}
