namespace OficinaMecanica.OS.Application.Contratos.Saga;

// Eventos publicados por Operações e consumidos pelo OS (asyncapi-saga-os.yaml, ADR-018).

public sealed record DiagnosisCompleted : MensagemSaga
{
    public required Guid ExecutionId { get; init; }
    public required DateTimeOffset PriceSnapshotAtUtc { get; init; }
    public required string Currency { get; init; }
    public required IReadOnlyList<PricedItem> Items { get; init; }

    protected override void ValidarPayload(List<string> erros)
    {
        ExigirMoeda(erros, Currency);
        if (Items.Count < 1)
            erros.Add("items deve ter ao menos um item.");
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i] is null)
                erros.Add($"items[{i}] não pode ser nulo.");
            else
                Items[i].Validar(erros, $"items[{i}]");
        }
    }
}

public sealed record DiagnosisRejected : Rejection;

public sealed record InventoryReserved : MensagemSaga
{
    public required Guid ReservationId { get; init; }
    public required IReadOnlyList<PecaQuantity> Items { get; init; }

    protected override void ValidarPayload(List<string> erros) => ValidarPecas(erros, Items, "items");
}

public sealed record InventoryReservationRejected : Rejection
{
    public IReadOnlyList<Guid>? UnavailablePecaIds { get; init; }
}

public sealed record ExecutionStarted : MensagemSaga
{
    public required Guid ExecutionId { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
}

public sealed record ExecutionStartRejected : Rejection
{
    public required Guid ExecutionId { get; init; }
}

public sealed record ExecutionCompleted : MensagemSaga
{
    public required Guid ExecutionId { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required IReadOnlyList<PecaQuantity> ConsumedItems { get; init; }

    protected override void ValidarPayload(List<string> erros) => ValidarPecas(erros, ConsumedItems, "consumedItems");
}

public sealed record ExecutionFailed : MensagemSaga
{
    public required string Reason { get; init; }
    public required Guid ExecutionId { get; init; }
    public required IReadOnlyList<PecaQuantity> ConsumedItems { get; init; }

    protected override void ValidarPayload(List<string> erros)
    {
        ExigirTexto(erros, Reason, "reason");
        ValidarPecas(erros, ConsumedItems, "consumedItems");
    }
}

public sealed record InventoryReleased : MensagemSaga
{
    public required Guid ReservationId { get; init; }
    public required IReadOnlyList<PecaQuantity> ReleasedItems { get; init; }

    protected override void ValidarPayload(List<string> erros) => ValidarPecas(erros, ReleasedItems, "releasedItems");
}
