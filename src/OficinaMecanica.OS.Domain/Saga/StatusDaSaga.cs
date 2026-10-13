using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Domain.Saga;

// Status da OS visível ao cliente, derivado do estado da Saga (tabela do ADR-017).
// Estados de desvio não mudam o status: o detalhe fica no histórico da Saga.
public static class StatusDaSaga
{
    public static StatusOrdemServico? Para(EstadoSaga estado) => estado switch
    {
        EstadoSaga.Diagnosing => StatusOrdemServico.EmDiagnostico,
        EstadoSaga.RequestingQuote or EstadoSaga.AwaitingApproval => StatusOrdemServico.AguardandoAprovacao,
        EstadoSaga.ReservingInventory or EstadoSaga.CreatingPayment or EstadoSaga.AwaitingPayment
            or EstadoSaga.ReconcilingPayment => StatusOrdemServico.AguardandoPagamento,
        EstadoSaga.StartingExecution or EstadoSaga.InExecution => StatusOrdemServico.EmExecucao,
        EstadoSaga.Completed => StatusOrdemServico.Finalizada,
        EstadoSaga.Cancelled or EstadoSaga.Compensated => StatusOrdemServico.Cancelada,
        _ => null
    };

    // Aplica à OS o status do estado atual da Saga, quando ele muda. Chamado na mesma transação da transição.
    public static void Sincronizar(Entities.OrdemServico os, SagaOrdemServico saga)
    {
        if (Para(saga.Estado) is { } status && status != os.Status)
            os.AlterarStatus(status);
    }
}
