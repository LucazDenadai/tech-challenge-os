namespace OficinaMecanica.OS.Application.Contratos.Saga;

// Eventos publicados pelo Billing e consumidos pelo OS (asyncapi-saga-os.yaml, ADR-018).

public sealed record QuoteReady : QuoteRef
{
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }

    protected override void ValidarPayload(List<string> erros)
    {
        base.ValidarPayload(erros);
        ExigirValor(erros, Amount, "amount");
        ExigirMoeda(erros, Currency);
    }
}

public sealed record QuoteRejected : Rejection;

public sealed record QuoteApproved : QuoteDecision;

public sealed record QuoteDeclined : QuoteDecision;

public sealed record QuoteExpired : QuoteRef;

public sealed record PaymentPending : PaymentRef;

public sealed record PaymentApproved : PaymentRef
{
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateTimeOffset ApprovedAtUtc { get; init; }

    protected override void ValidarPayload(List<string> erros)
    {
        ExigirValor(erros, Amount, "amount");
        ExigirMoeda(erros, Currency);
    }
}

public sealed record PaymentDeclined : PaymentRefWithReason;

public sealed record PaymentOutcomeUnknown : PaymentRefWithReason;

public sealed record PaymentRefunded : RefundResult;

public sealed record PaymentRefundPending : RefundResult;

public sealed record PaymentRefundFailed : RefundResult
{
    public required string Reason { get; init; }

    protected override void ValidarPayload(List<string> erros) => ExigirTexto(erros, Reason, "reason");
}
