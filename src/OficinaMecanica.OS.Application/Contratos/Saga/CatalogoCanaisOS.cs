namespace OficinaMecanica.OS.Application.Contratos.Saga;

public sealed record CanalConsumido(string Endereco, string MessageType, int SchemaVersion, string Produtor, Type TipoMensagem);

// Canais cujo consumidor é o OS no AsyncAPI (ADR-018). Um teste de contrato compara esta lista com a spec.
public static class CatalogoCanaisOS
{
    public static readonly IReadOnlyList<CanalConsumido> Todos =
    [
        Canal<DiagnosisCompleted>("saga-os.diagnosis-completed.v1", "operacoes"),
        Canal<DiagnosisRejected>("saga-os.diagnosis-rejected.v1", "operacoes"),
        Canal<QuoteReady>("saga-os.quote-ready.v1", "billing"),
        Canal<QuoteRejected>("saga-os.quote-rejected.v1", "billing"),
        Canal<QuoteApproved>("saga-os.quote-approved.v1", "billing"),
        Canal<QuoteDeclined>("saga-os.quote-declined.v1", "billing"),
        Canal<QuoteExpired>("saga-os.quote-expired.v1", "billing"),
        Canal<InventoryReserved>("saga-os.inventory-reserved.v1", "operacoes"),
        Canal<InventoryReservationRejected>("saga-os.inventory-reservation-rejected.v1", "operacoes"),
        Canal<PaymentPending>("saga-os.payment-pending.v1", "billing"),
        Canal<PaymentApproved>("saga-os.payment-approved.v1", "billing"),
        Canal<PaymentDeclined>("saga-os.payment-declined.v1", "billing"),
        Canal<PaymentOutcomeUnknown>("saga-os.payment-outcome-unknown.v1", "billing"),
        Canal<ExecutionStarted>("saga-os.execution-started.v1", "operacoes"),
        Canal<ExecutionStartRejected>("saga-os.execution-start-rejected.v1", "operacoes"),
        Canal<ExecutionCompleted>("saga-os.execution-completed.v1", "operacoes"),
        Canal<ExecutionFailed>("saga-os.execution-failed.v1", "operacoes"),
        Canal<InventoryReleased>("saga-os.inventory-released.v1", "operacoes"),
        Canal<PaymentRefunded>("saga-os.payment-refunded.v1", "billing"),
        Canal<PaymentRefundPending>("saga-os.payment-refund-pending.v1", "billing"),
        Canal<PaymentRefundFailed>("saga-os.payment-refund-failed.v1", "billing")
    ];

    public static CanalConsumido? Obter(string endereco) => Todos.FirstOrDefault(c => c.Endereco == endereco);

    // Todos os canais são v1: o messageType é o nome do DTO + ".v1", como `name` das mensagens na spec.
    private static CanalConsumido Canal<T>(string endereco, string produtor) where T : MensagemSaga
        => new(endereco, $"{typeof(T).Name}.v1", 1, produtor, typeof(T));
}
