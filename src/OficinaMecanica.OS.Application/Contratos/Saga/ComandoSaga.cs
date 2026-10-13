using System.Text.Json;
using System.Text.Json.Serialization;
using OficinaMecanica.OS.Domain.Saga;

namespace OficinaMecanica.OS.Application.Contratos.Saga;

// Monta e serializa os comandos que o OS publica. O envelope vem da Saga: correlationId = Id da Saga,
// causationId = messageId do evento que levou ao comando. Ausente no DiagnosisRequested e nos comandos
// disparados por prazo, retomada pelo Admin ou cancelamento manual, que não vêm de uma mensagem.
// A idempotencyKey é estável por OS e etapa: reprocessar o mesmo evento gera a mesma chave, e o participante deduplica.
public static class ComandoSaga
{
    private static readonly JsonSerializerOptions _opcoes = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serializar(MensagemSaga mensagem) => JsonSerializer.Serialize(mensagem, mensagem.GetType(), _opcoes);

    public static string Chave(SagaOrdemServico saga, string etapa) => $"os:{saga.OsId}:{etapa}";

    public static DiagnosisRequested Diagnostico(SagaOrdemServico saga) => new()
    {
        MessageId = Guid.NewGuid(), MessageType = Tipo<DiagnosisRequested>(), SchemaVersion = 1, Producer = CatalogoCanaisOS.Produtor,
        OccurredAtUtc = DateTimeOffset.UtcNow, CorrelationId = saga.Id, OsId = saga.OsId, FilialId = saga.FilialId,
        IdempotencyKey = Chave(saga, "diagnosis"),
        VeiculoId = saga.VeiculoId
    };

    // O MVP pede uma única versão de orçamento por OS (ADR-017); por isso a chave termina em ":1".
    public static QuoteRequested Orcamento(SagaOrdemServico saga, Guid? causationId) => new()
    {
        MessageId = Guid.NewGuid(), MessageType = Tipo<QuoteRequested>(), SchemaVersion = 1, Producer = CatalogoCanaisOS.Produtor,
        OccurredAtUtc = DateTimeOffset.UtcNow, CorrelationId = saga.Id, CausationId = causationId, OsId = saga.OsId, FilialId = saga.FilialId,
        IdempotencyKey = Chave(saga, "quote:1"),
        ClienteId = saga.ClienteId,
        PriceSnapshotAtUtc = saga.PrecoSnapshotEm!.Value,
        Currency = saga.MoedaDiagnostico!,
        Items = saga.Itens.Select(i => new PricedItem
        {
            ItemId = i.ItemId, Type = i.Tipo.ToString(), Description = i.Descricao, Quantity = i.Quantidade, UnitPrice = i.PrecoUnitario
        }).ToList()
    };

    public static InventoryReservationRequested Reserva(SagaOrdemServico saga, Guid? causationId) => new()
    {
        MessageId = Guid.NewGuid(), MessageType = Tipo<InventoryReservationRequested>(), SchemaVersion = 1, Producer = CatalogoCanaisOS.Produtor,
        OccurredAtUtc = DateTimeOffset.UtcNow, CorrelationId = saga.Id, CausationId = causationId, OsId = saga.OsId, FilialId = saga.FilialId,
        IdempotencyKey = Chave(saga, "reservation"),
        Items = saga.PecasParaReservar.Select(p => new PecaQuantity { PecaId = p.PecaId, Quantity = p.Quantidade }).ToList()
    };

    public static PaymentCreationRequested Pagamento(SagaOrdemServico saga, Guid? causationId, string payerEmail) => new()
    {
        MessageId = Guid.NewGuid(), MessageType = Tipo<PaymentCreationRequested>(), SchemaVersion = 1, Producer = CatalogoCanaisOS.Produtor,
        OccurredAtUtc = DateTimeOffset.UtcNow, CorrelationId = saga.Id, CausationId = causationId, OsId = saga.OsId, FilialId = saga.FilialId,
        IdempotencyKey = Chave(saga, $"payment:{saga.QuoteVersion}"),
        QuoteId = saga.QuoteId!.Value,
        QuoteVersion = saga.QuoteVersion!.Value,
        Amount = saga.ValorOrcamento!.Value,
        Currency = saga.MoedaOrcamento!,
        PayerEmail = payerEmail
    };

    public static ExecutionStartRequested InicioExecucao(SagaOrdemServico saga, Guid? causationId) => new()
    {
        MessageId = Guid.NewGuid(), MessageType = Tipo<ExecutionStartRequested>(), SchemaVersion = 1, Producer = CatalogoCanaisOS.Produtor,
        OccurredAtUtc = DateTimeOffset.UtcNow, CorrelationId = saga.Id, CausationId = causationId, OsId = saga.OsId, FilialId = saga.FilialId,
        IdempotencyKey = Chave(saga, "execution"),
        ExecutionId = saga.ExecutionId!.Value,
        ReservationId = saga.ReservationId
    };

    public static InventoryReleaseRequested Liberacao(SagaOrdemServico saga, Guid? causationId) => new()
    {
        MessageId = Guid.NewGuid(), MessageType = Tipo<InventoryReleaseRequested>(), SchemaVersion = 1, Producer = CatalogoCanaisOS.Produtor,
        OccurredAtUtc = DateTimeOffset.UtcNow, CorrelationId = saga.Id, CausationId = causationId, OsId = saga.OsId, FilialId = saga.FilialId,
        IdempotencyKey = Chave(saga, "release"),
        Reason = Motivo(saga, "Compensação da Saga."),
        ReservationId = saga.ReservationId!.Value
    };

    // Estorno total do valor aprovado (ADR-017; estorno parcial é evolução no ADR-020).
    public static PaymentRefundRequested Estorno(SagaOrdemServico saga, Guid? causationId) => new()
    {
        MessageId = Guid.NewGuid(), MessageType = Tipo<PaymentRefundRequested>(), SchemaVersion = 1, Producer = CatalogoCanaisOS.Produtor,
        OccurredAtUtc = DateTimeOffset.UtcNow, CorrelationId = saga.Id, CausationId = causationId, OsId = saga.OsId, FilialId = saga.FilialId,
        IdempotencyKey = Chave(saga, "refund"),
        Reason = Motivo(saga, "Compensação da Saga."),
        PaymentId = saga.PaymentId!.Value,
        Amount = saga.ValorOrcamento!.Value,
        Currency = saga.MoedaOrcamento!
    };

    public static ExecutionCancelRequested CancelamentoExecucao(SagaOrdemServico saga, Guid? causationId) => new()
    {
        MessageId = Guid.NewGuid(), MessageType = Tipo<ExecutionCancelRequested>(), SchemaVersion = 1, Producer = CatalogoCanaisOS.Produtor,
        OccurredAtUtc = DateTimeOffset.UtcNow, CorrelationId = saga.Id, CausationId = causationId, OsId = saga.OsId, FilialId = saga.FilialId,
        IdempotencyKey = Chave(saga, "cancel-execution"),
        Reason = Motivo(saga, "Cancelamento da OS."),
        ExecutionId = saga.ExecutionId!.Value
    };

    // Monta os comandos de uma etapa (ComandosDaEtapa da Saga). O e-mail só é usado no pedido de pagamento.
    public static IReadOnlyList<MensagemSaga> DaEtapa(SagaOrdemServico saga, IEnumerable<ComandoEtapa> etapas, Guid? causationId, string? payerEmail)
        => etapas.Select(etapa => etapa switch
        {
            ComandoEtapa.Diagnostico => (MensagemSaga)Diagnostico(saga),
            ComandoEtapa.Orcamento => Orcamento(saga, causationId),
            ComandoEtapa.Reserva => Reserva(saga, causationId),
            ComandoEtapa.Pagamento => Pagamento(saga, causationId, payerEmail
                ?? throw new InvalidOperationException("O pedido de pagamento exige o e-mail do cliente.")),
            ComandoEtapa.InicioExecucao => InicioExecucao(saga, causationId),
            ComandoEtapa.Liberacao => Liberacao(saga, causationId),
            ComandoEtapa.Estorno => Estorno(saga, causationId),
            ComandoEtapa.CancelamentoExecucao => CancelamentoExecucao(saga, causationId),
            _ => throw new ArgumentOutOfRangeException(nameof(etapas), etapa, "Etapa sem comando.")
        }).ToList();

    // Motivo legível e sem dados pessoais (spec, schema Reason).
    private static string Motivo(SagaOrdemServico saga, string padrao) => string.IsNullOrWhiteSpace(saga.Motivo) ? padrao : saga.Motivo;

    private static string Tipo<T>() where T : MensagemSaga => CatalogoCanaisOS.Publicado(typeof(T)).MessageType;
}
