namespace OficinaMecanica.OS.Application.Contratos.Saga;

// Bases que espelham os schemas reutilizados da spec (components/schemas): cada campo é declarado uma vez.
// Cada messageType continua com seu próprio record, pois tem canal próprio no AsyncAPI (ADR-018).

// Rejection = Envelope + Reason.
public abstract record Rejection : MensagemSaga
{
    public required string Reason { get; init; }

    protected override void ValidarPayload(List<string> erros) => ExigirTexto(erros, Reason, "reason");
}

// QuoteRef = quoteId + quoteVersion.
public abstract record QuoteRef : MensagemSaga
{
    public required Guid QuoteId { get; init; }
    public required int QuoteVersion { get; init; }

    protected override void ValidarPayload(List<string> erros) => ExigirMinimo(erros, QuoteVersion, 1, "quoteVersion");
}

// QuoteDecision = Envelope + QuoteRef + decidedAtUtc.
public abstract record QuoteDecision : QuoteRef
{
    public required DateTimeOffset DecidedAtUtc { get; init; }
}

// PaymentRef = Envelope + paymentId + providerReference opcional.
public abstract record PaymentRef : MensagemSaga
{
    public required Guid PaymentId { get; init; }
    public string? ProviderReference { get; init; }
}

// PaymentRef + Reason (recusa e resultado desconhecido).
public abstract record PaymentRefWithReason : PaymentRef
{
    public required string Reason { get; init; }

    protected override void ValidarPayload(List<string> erros) => ExigirTexto(erros, Reason, "reason");
}

// RefundResult = Envelope + paymentId + providerRefundReference opcional.
public abstract record RefundResult : MensagemSaga
{
    public required Guid PaymentId { get; init; }
    public string? ProviderRefundReference { get; init; }
}
