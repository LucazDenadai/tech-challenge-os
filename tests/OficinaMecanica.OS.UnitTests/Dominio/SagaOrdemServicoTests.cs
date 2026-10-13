using OficinaMecanica.OS.Domain.Enums;
using OficinaMecanica.OS.Domain.Saga;
using DomainOS = OficinaMecanica.OS.Domain.Entities.OrdemServico;

namespace OficinaMecanica.OS.UnitTests.Dominio;

public class SagaOrdemServicoTests
{
    private static readonly Guid _execucao = Guid.NewGuid();
    private static readonly Guid _quote = Guid.NewGuid();
    private static readonly Guid _peca = Guid.NewGuid();

    private static Causa C(string tipo) => new(tipo, Guid.NewGuid());

    private static (DomainOS Os, SagaOrdemServico Saga) Nova()
    {
        var os = new DomainOS("OS-001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "");
        return (os, SagaOrdemServico.Iniciar(os));
    }

    private static ItemDiagnostico[] Itens(bool comPeca = true) => comPeca
        ?
        [
            new(_peca, TipoItemDiagnostico.Peca, "Filtro", 1, 45.90m),
            new(_peca, TipoItemDiagnostico.Peca, "Filtro", 2, 45.90m),
            new(Guid.NewGuid(), TipoItemDiagnostico.Servico, "Troca", 1, 80m)
        ]
        : [new(Guid.NewGuid(), TipoItemDiagnostico.Servico, "Troca", 1, 80m)];

    // Leva a Saga até o estado pedido pelo caminho normal.
    private static SagaOrdemServico Em(EstadoSaga estado, bool comPeca = true)
    {
        var (_, saga) = Nova();
        var passos = new Func<bool>[]
        {
            () => saga.RegistrarDiagnostico(C("DiagnosisCompleted.v1"), _execucao, "BRL", DateTimeOffset.UtcNow, Itens(comPeca)),
            () => saga.RegistrarOrcamento(C("QuoteReady.v1"), _quote, 1, 217.70m, "BRL"),
            () => saga.AprovarOrcamento(C("QuoteApproved.v1"), _quote, 1),
            () => saga.RegistrarReserva(C("InventoryReserved.v1"), Guid.NewGuid()),
            () => saga.RegistrarPagamentoPendente(C("PaymentPending.v1"), Guid.NewGuid()),
            () => saga.AprovarPagamento(C("PaymentApproved.v1"), saga.PaymentId!.Value, 217.70m, "BRL"),
            () => saga.RegistrarInicioExecucao(C("ExecutionStarted.v1"), _execucao),
            () => saga.ConcluirExecucao(C("ExecutionCompleted.v1"), _execucao)
        };

        foreach (var passo in passos)
        {
            if (saga.Estado == estado) break;
            Assert.True(passo());
        }

        Assert.Equal(estado, saga.Estado);
        return saga;
    }

    [Fact]
    public void Iniciar_FicaEmDiagnosingComTransicaoDeAbertura()
    {
        var (os, saga) = Nova();

        Assert.Equal(EstadoSaga.Diagnosing, saga.Estado);
        Assert.Equal(os.Id, saga.OsId);
        Assert.Equal(os.FilialId, saga.FilialId);
        Assert.NotEqual(os.Id, saga.Id);
        var transicao = Assert.Single(saga.Transicoes);
        Assert.Null(transicao.EstadoAnterior);
        Assert.Equal("AberturaOS", transicao.Causa);
    }

    [Fact]
    public void FluxoNormal_ChegaACompletedRegistrandoCadaTransicao()
    {
        var saga = Em(EstadoSaga.Completed);

        Assert.True(saga.Terminada);
        Assert.Equal(9, saga.Transicoes.Count);
        Assert.Equal(
            [EstadoSaga.Diagnosing, EstadoSaga.RequestingQuote, EstadoSaga.AwaitingApproval, EstadoSaga.ReservingInventory,
             EstadoSaga.CreatingPayment, EstadoSaga.AwaitingPayment, EstadoSaga.StartingExecution, EstadoSaga.InExecution, EstadoSaga.Completed],
            saga.Transicoes.Select(t => t.EstadoNovo));
    }

    [Fact]
    public void PecasParaReservar_SomaPorPecaEIgnoraServicos()
    {
        var saga = Em(EstadoSaga.RequestingQuote);

        var peca = Assert.Single(saga.PecasParaReservar);
        Assert.Equal((_peca, 3), peca);
    }

    [Fact]
    public void MensagemForaDoEstado_NaoMudaNada()
    {
        var saga = Em(EstadoSaga.AwaitingApproval);
        var transicoes = saga.Transicoes.Count;

        Assert.False(saga.RegistrarReserva(C("InventoryReserved.v1"), Guid.NewGuid()));
        Assert.False(saga.RegistrarDiagnostico(C("DiagnosisCompleted.v1"), _execucao, "BRL", DateTimeOffset.UtcNow, Itens()));
        Assert.False(saga.ConcluirExecucao(C("ExecutionCompleted.v1"), _execucao));

        Assert.Equal(EstadoSaga.AwaitingApproval, saga.Estado);
        Assert.Equal(transicoes, saga.Transicoes.Count);
    }

    [Fact]
    public void DecisaoDeOutraVersao_NaoSeAplica()
    {
        var saga = Em(EstadoSaga.AwaitingApproval);

        Assert.False(saga.AprovarOrcamento(C("QuoteApproved.v1"), _quote, 2));
        Assert.False(saga.AprovarOrcamento(C("QuoteApproved.v1"), Guid.NewGuid(), 1));
        Assert.False(saga.EncerrarOrcamento(C("QuoteDeclined.v1"), _quote, 2, "Recusado"));
        Assert.Equal(EstadoSaga.AwaitingApproval, saga.Estado);
    }

    [Fact]
    public void OrcamentoSemPecas_PulaAReservaEVaiAoPagamento()
    {
        // Serviço sem peça (ADR-020).
        var saga = Em(EstadoSaga.AwaitingApproval, comPeca: false);

        Assert.True(saga.AprovarOrcamento(C("QuoteApproved.v1"), _quote, 1));

        Assert.Equal(EstadoSaga.CreatingPayment, saga.Estado);
        Assert.Equal([ComandoEtapa.Pagamento], saga.ComandosDaEtapa());
    }

    [Fact]
    public void PagamentoAprovadoAntesDoPendente_Aceito()
    {
        // A ordem entre canais diferentes não é garantida (CARD-40a).
        var saga = Em(EstadoSaga.CreatingPayment);
        var pagamento = Guid.NewGuid();

        Assert.True(saga.AprovarPagamento(C("PaymentApproved.v1"), pagamento, 217.70m, "BRL"));
        Assert.False(saga.RegistrarPagamentoPendente(C("PaymentPending.v1"), pagamento));

        Assert.Equal(EstadoSaga.StartingExecution, saga.Estado);
        Assert.Equal(pagamento, saga.PaymentId);
    }

    [Fact]
    public void PagamentoAprovadoDeOutraTentativa_NaoSeAplica()
    {
        var saga = Em(EstadoSaga.AwaitingPayment);

        Assert.False(saga.AprovarPagamento(C("PaymentApproved.v1"), Guid.NewGuid(), 217.70m, "BRL"));
        Assert.Equal(EstadoSaga.AwaitingPayment, saga.Estado);
    }

    [Theory]
    [InlineData(100.00, "BRL")]
    [InlineData(217.70, "USD")]
    public void PagamentoComValorOuMoedaDivergentes_VaiParaAcaoManualSemIniciarExecucao(double valor, string moeda)
    {
        var saga = Em(EstadoSaga.AwaitingPayment);

        Assert.True(saga.AprovarPagamento(C("PaymentApproved.v1"), saga.PaymentId!.Value, (decimal)valor, moeda));

        Assert.Equal(EstadoSaga.ManualActionRequired, saga.Estado);
    }

    [Fact]
    public void ExecucaoDeOutroId_NaoSeAplica()
    {
        var saga = Em(EstadoSaga.StartingExecution);

        Assert.False(saga.RegistrarInicioExecucao(C("ExecutionStarted.v1"), Guid.NewGuid()));
        Assert.Equal(EstadoSaga.StartingExecution, saga.Estado);
    }

    [Theory]
    [InlineData(EstadoSaga.Diagnosing)]
    [InlineData(EstadoSaga.RequestingQuote)]
    [InlineData(EstadoSaga.AwaitingApproval)]
    [InlineData(EstadoSaga.ReservingInventory)]
    public void Rejeicoes_EncerramEmCancelledComMotivo(EstadoSaga estado)
    {
        var saga = Em(estado);

        var aplicada = estado switch
        {
            EstadoSaga.Diagnosing => saga.RejeitarDiagnostico(C("DiagnosisRejected.v1"), "Filial inválida"),
            EstadoSaga.RequestingQuote => saga.RejeitarOrcamento(C("QuoteRejected.v1"), "Item sem preço"),
            EstadoSaga.AwaitingApproval => saga.EncerrarOrcamento(C("QuoteDeclined.v1"), _quote, 1, "Recusado pelo cliente"),
            _ => saga.RejeitarReserva(C("InventoryReservationRejected.v1"), "Sem saldo")
        };

        Assert.True(aplicada);
        Assert.Equal(EstadoSaga.Cancelled, saga.Estado);
        Assert.True(saga.Terminada);
        Assert.NotNull(saga.Motivo);
    }

    [Theory]
    [InlineData(EstadoSaga.Diagnosing, StatusOrdemServico.EmDiagnostico)]
    [InlineData(EstadoSaga.RequestingQuote, StatusOrdemServico.AguardandoAprovacao)]
    [InlineData(EstadoSaga.AwaitingApproval, StatusOrdemServico.AguardandoAprovacao)]
    [InlineData(EstadoSaga.ReservingInventory, StatusOrdemServico.AguardandoPagamento)]
    [InlineData(EstadoSaga.ReconcilingPayment, StatusOrdemServico.AguardandoPagamento)]
    [InlineData(EstadoSaga.StartingExecution, StatusOrdemServico.EmExecucao)]
    [InlineData(EstadoSaga.Completed, StatusOrdemServico.Finalizada)]
    [InlineData(EstadoSaga.Cancelled, StatusOrdemServico.Cancelada)]
    [InlineData(EstadoSaga.Compensated, StatusOrdemServico.Cancelada)]
    public void StatusDaSaga_SegueATabelaDoAdr017(EstadoSaga estado, StatusOrdemServico status)
        => Assert.Equal(status, StatusDaSaga.Para(estado));

    [Theory]
    [InlineData(EstadoSaga.ManualActionRequired)]
    [InlineData(EstadoSaga.Compensating)]
    [InlineData(EstadoSaga.ReleasingReservation)]
    [InlineData(EstadoSaga.CompensationPending)]
    public void StatusDaSaga_DesviosMantemStatusAnterior(EstadoSaga estado) => Assert.Null(StatusDaSaga.Para(estado));

    [Fact]
    public void Sincronizar_AcompanhaOFluxoNormalAteFinalizada()
    {
        var (os, saga) = Nova();
        var passos = new Func<bool>[]
        {
            () => saga.RegistrarDiagnostico(C("DiagnosisCompleted.v1"), _execucao, "BRL", DateTimeOffset.UtcNow, Itens()),
            () => saga.RegistrarOrcamento(C("QuoteReady.v1"), _quote, 1, 217.70m, "BRL"),
            () => saga.AprovarOrcamento(C("QuoteApproved.v1"), _quote, 1),
            () => saga.RegistrarReserva(C("InventoryReserved.v1"), Guid.NewGuid()),
            () => saga.AprovarPagamento(C("PaymentApproved.v1"), Guid.NewGuid(), 217.70m, "BRL"),
            () => saga.RegistrarInicioExecucao(C("ExecutionStarted.v1"), _execucao),
            () => saga.ConcluirExecucao(C("ExecutionCompleted.v1"), _execucao)
        };

        foreach (var passo in passos)
        {
            Assert.True(passo());
            StatusDaSaga.Sincronizar(os, saga);
        }

        Assert.Equal(StatusOrdemServico.Finalizada, os.Status);
        // Uma linha de histórico por mudança de status, não por transição da Saga.
        Assert.Equal(4, os.Historico.Count);
    }
}
