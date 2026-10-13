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
    // Desvios (CARD-40b). CompensationPending é do ADR-017, mas não é usado: estorno pendente fica em Compensating.
    ReleasingReservation = 12,
    Compensating = 13,
    CompensationPending = 14,
    ManualActionRequired = 15,
    Compensated = 16,
    // Operações decide um pedido de cancelamento de execução (ADR-020).
    CancellingExecution = 17
}

// Por que a Saga parou em ManualActionRequired. Define o que o Admin pode fazer (ADR-020).
public enum CausaAcaoManual
{
    PrazoEsgotado = 1,
    PagamentoDivergente = 2,
    EstornoFalhou = 3
}

// Comandos que uma etapa precisa ter enviado. A aplicação monta o contrato de cada um.
public enum ComandoEtapa
{
    Diagnostico = 1,
    Orcamento = 2,
    Reserva = 3,
    Pagamento = 4,
    InicioExecucao = 5,
    Liberacao = 6,
    Estorno = 7,
    CancelamentoExecucao = 8
}

public enum ResultadoCancelamento
{
    // Encerrada sem nada a desfazer (204).
    Encerrada = 1,
    // Pedido registrado; a Saga compensa ou espera um participante (202).
    EmAndamento = 2,
    // Ação manual ou estado terminal (422).
    Recusado = 3
}

public enum AcaoResolucao
{
    Cancelar = 1,
    Retomar = 2
}

public enum TipoItemDiagnostico
{
    Peca = 1,
    Servico = 2
}
