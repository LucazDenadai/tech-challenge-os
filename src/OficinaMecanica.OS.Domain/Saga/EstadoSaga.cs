namespace OficinaMecanica.OS.Domain.Saga;

// Estados persistidos da Saga (ADR-017). Os nomes seguem o ADR para serem rastreáveis na documentação.
public enum EstadoSaga
{
    Diagnosing = 1,
    RequestingQuote = 2,
    AwaitingApproval = 3,
    ReservingInventory = 4,
    CreatingPayment = 5,
    AwaitingPayment = 6,
    ReconcilingPayment = 7,
    StartingExecution = 8,
    InExecution = 9,
    Completed = 10,
    Cancelled = 11,
    // Desvios do CARD-40b. ManualActionRequired já é usado no CARD-40a para casos sem saída automática.
    ReleasingReservation = 12,
    Compensating = 13,
    CompensationPending = 14,
    ManualActionRequired = 15,
    Compensated = 16
}

public enum TipoItemDiagnostico
{
    Peca = 1,
    Servico = 2
}
