using OficinaMecanica.OS.Domain.Saga;
using DomainOS = OficinaMecanica.OS.Domain.Entities.OrdemServico;

namespace OficinaMecanica.OS.UnitTests.Dominio;

// Desvios e compensações da Saga (CARD-40b, ADR-017 e ADR-020).
public class SagaCompensacaoTests
{
    private static readonly Guid _execucao = Guid.NewGuid();
    private static readonly Guid _quote = Guid.NewGuid();
    private static readonly Guid _reserva = Guid.NewGuid();
    private static readonly Guid _pagamento = Guid.NewGuid();

    private static Causa C(string tipo) => new(tipo, Guid.NewGuid());

    // Leva a Saga pelo caminho normal até o estado pedido. comPeca=false usa diagnóstico só com serviço.
    private static SagaOrdemServico Em(EstadoSaga estado, bool comPeca = true)
    {
        var os = new DomainOS("OS-001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "");
        var saga = SagaOrdemServico.Iniciar(os);
        ItemDiagnostico[] itens = comPeca
            ? [new(Guid.NewGuid(), TipoItemDiagnostico.Peca, "Filtro", 1, 100m)]
            : [new(Guid.NewGuid(), TipoItemDiagnostico.Servico, "Balanceamento", 1, 100m)];
        var passos = new Func<bool>[]
        {
            () => saga.RegistrarDiagnostico(C("DiagnosisCompleted.v1"), _execucao, "BRL", DateTimeOffset.UtcNow, itens),
            () => saga.RegistrarOrcamento(C("QuoteReady.v1"), _quote, 1, 100m, "BRL"),
            () => saga.AprovarOrcamento(C("QuoteApproved.v1"), _quote, 1),
            () => saga.Estado == EstadoSaga.ReservingInventory && saga.RegistrarReserva(C("InventoryReserved.v1"), _reserva),
            () => saga.RegistrarPagamentoPendente(C("PaymentPending.v1"), _pagamento),
            () => saga.AprovarPagamento(C("PaymentApproved.v1"), _pagamento, 100m, "BRL"),
            () => saga.RegistrarInicioExecucao(C("ExecutionStarted.v1"), _execucao)
        };

        foreach (var passo in passos)
        {
            if (saga.Estado == estado) break;
            if (saga.Estado == EstadoSaga.CreatingPayment && !comPeca && estado != EstadoSaga.CreatingPayment)
            {
                Assert.True(saga.RegistrarPagamentoPendente(C("PaymentPending.v1"), _pagamento));
                continue;
            }
            passo();
        }

        Assert.Equal(estado, saga.Estado);
        return saga;
    }

    // ── Pagamento recusado ou incerto ──────────────────────────────────────────

    [Fact]
    public void PagamentoRecusado_LiberaAReservaEEncerraSoDepoisDaConfirmacao()
    {
        var saga = Em(EstadoSaga.AwaitingPayment);

        Assert.True(saga.RecusarPagamento(C("PaymentDeclined.v1"), _pagamento, "Pix expirado"));
        Assert.Equal(EstadoSaga.ReleasingReservation, saga.Estado);
        Assert.Equal([ComandoEtapa.Liberacao], saga.ComandosDaEtapa());

        Assert.True(saga.RegistrarLiberacao(C("InventoryReleased.v1"), _reserva));
        Assert.Equal(EstadoSaga.Cancelled, saga.Estado);
    }

    [Fact]
    public void PagamentoRecusadoSemReserva_EncerraDireto()
    {
        var saga = Em(EstadoSaga.AwaitingPayment, comPeca: false);

        Assert.True(saga.RecusarPagamento(C("PaymentDeclined.v1"), _pagamento, "Recusado"));

        Assert.Equal(EstadoSaga.Cancelled, saga.Estado);
    }

    [Fact]
    public void ResultadoDesconhecido_ReconciliaSemComandoEDepoisSegueOResultado()
    {
        var saga = Em(EstadoSaga.AwaitingPayment);

        Assert.True(saga.RegistrarResultadoDesconhecido(C("PaymentOutcomeUnknown.v1"), _pagamento, "Timeout"));
        Assert.Equal(EstadoSaga.ReconcilingPayment, saga.Estado);
        // Mantém a reserva e não cobra de novo (ADR-017).
        Assert.Empty(saga.ComandosDaEtapa());

        Assert.True(saga.AprovarPagamento(C("PaymentApproved.v1"), _pagamento, 100m, "BRL"));
        Assert.Equal(EstadoSaga.StartingExecution, saga.Estado);
    }

    // ── Compensação depois do pagamento ────────────────────────────────────────

    [Theory]
    [InlineData("rejeitado")]
    [InlineData("falhou")]
    public void FalhaDepoisDoPagamento_EstornaELiberaESoTerminaComAsDuasConfirmacoes(string caso)
    {
        var saga = Em(caso == "rejeitado" ? EstadoSaga.StartingExecution : EstadoSaga.InExecution);

        Assert.True(caso == "rejeitado"
            ? saga.RejeitarInicioExecucao(C("ExecutionStartRejected.v1"), _execucao, "Reserva inexistente")
            : saga.FalharExecucao(C("ExecutionFailed.v1"), _execucao, "Peça danificada"));
        Assert.Equal(EstadoSaga.Compensating, saga.Estado);
        Assert.Equal([ComandoEtapa.Estorno, ComandoEtapa.Liberacao], saga.ComandosDaEtapa());

        Assert.True(saga.RegistrarEstorno(C("PaymentRefunded.v1"), _pagamento));
        Assert.Equal(EstadoSaga.Compensating, saga.Estado);
        Assert.Equal([ComandoEtapa.Liberacao], saga.ComandosDaEtapa());

        Assert.True(saga.RegistrarLiberacao(C("InventoryReleased.v1"), _reserva));
        Assert.Equal(EstadoSaga.Compensated, saga.Estado);
        Assert.True(saga.Terminada);
    }

    [Fact]
    public void FalhaSemReserva_TerminaSoComOEstorno()
    {
        var saga = Em(EstadoSaga.InExecution, comPeca: false);

        saga.FalharExecucao(C("ExecutionFailed.v1"), _execucao, "Desistência técnica");
        Assert.Equal([ComandoEtapa.Estorno], saga.ComandosDaEtapa());
        saga.RegistrarEstorno(C("PaymentRefunded.v1"), _pagamento);

        Assert.Equal(EstadoSaga.Compensated, saga.Estado);
    }

    [Fact]
    public void EstornoPendente_FicaExplicitoSemCompensar()
    {
        var saga = Em(EstadoSaga.InExecution);
        saga.FalharExecucao(C("ExecutionFailed.v1"), _execucao, "Falha");
        saga.RegistrarLiberacao(C("InventoryReleased.v1"), _reserva);

        Assert.True(saga.RegistrarEstornoPendente(C("PaymentRefundPending.v1"), _pagamento));

        Assert.Equal(EstadoSaga.Compensating, saga.Estado);
        Assert.True(saga.EstornoPendente);
        Assert.False(saga.Terminada);
    }

    [Fact]
    public void EstornoFalhou_VaiParaAcaoManual()
    {
        var saga = Em(EstadoSaga.InExecution);
        saga.FalharExecucao(C("ExecutionFailed.v1"), _execucao, "Falha");

        Assert.True(saga.FalharEstorno(C("PaymentRefundFailed.v1"), _pagamento, "Provedor recusou"));

        Assert.Equal(EstadoSaga.ManualActionRequired, saga.Estado);
        Assert.Equal(CausaAcaoManual.EstornoFalhou, saga.CausaManual);
        Assert.Equal(EstadoSaga.Compensating, saga.EstadoRetomada);
    }

    [Fact]
    public void ConfirmacaoRepetida_NaoSeAplicaDuasVezes()
    {
        var saga = Em(EstadoSaga.InExecution);
        saga.FalharExecucao(C("ExecutionFailed.v1"), _execucao, "Falha");

        Assert.True(saga.RegistrarEstorno(C("PaymentRefunded.v1"), _pagamento));
        Assert.False(saga.RegistrarEstorno(C("PaymentRefunded.v1"), _pagamento));
        Assert.False(saga.RegistrarLiberacao(C("InventoryReleased.v1"), Guid.NewGuid()));
    }

    // ── Cancelamento manual (ADR-020) ──────────────────────────────────────────

    [Theory]
    [InlineData(EstadoSaga.Diagnosing)]
    [InlineData(EstadoSaga.RequestingQuote)]
    [InlineData(EstadoSaga.AwaitingApproval)]
    public void CancelarAntesDeQualquerEfeito_Encerra(EstadoSaga estado)
    {
        var saga = Em(estado);

        Assert.Equal(ResultadoCancelamento.Encerrada, saga.SolicitarCancelamento("Cliente desistiu"));
        Assert.Equal(EstadoSaga.Cancelled, saga.Estado);
    }

    [Fact]
    public void CancelarComReservaPendente_LiberaQuandoAReservaChega()
    {
        var saga = Em(EstadoSaga.ReservingInventory);

        Assert.Equal(ResultadoCancelamento.EmAndamento, saga.SolicitarCancelamento("Cliente desistiu"));
        Assert.Equal(EstadoSaga.ReservingInventory, saga.Estado);

        saga.RegistrarReserva(C("InventoryReserved.v1"), _reserva);
        Assert.Equal(EstadoSaga.ReleasingReservation, saga.Estado);
    }

    [Fact]
    public void CancelarAguardandoPagamento_EstornaSeOPagamentoForAprovado()
    {
        var saga = Em(EstadoSaga.AwaitingPayment);
        saga.SolicitarCancelamento("Cliente desistiu");

        saga.AprovarPagamento(C("PaymentApproved.v1"), _pagamento, 100m, "BRL");

        Assert.Equal(EstadoSaga.Compensating, saga.Estado);
        Assert.Equal([ComandoEtapa.Estorno, ComandoEtapa.Liberacao], saga.ComandosDaEtapa());
    }

    [Fact]
    public void CancelarComInicioPendente_PedeCancelamentoQuandoAExecucaoEntraNaFila()
    {
        var saga = Em(EstadoSaga.StartingExecution);
        saga.SolicitarCancelamento("Cliente desistiu");

        saga.RegistrarInicioExecucao(C("ExecutionStarted.v1"), _execucao);

        Assert.Equal(EstadoSaga.CancellingExecution, saga.Estado);
    }

    [Fact]
    public void CancelarEmExecucao_ExecucaoCanceladaCompensa()
    {
        var saga = Em(EstadoSaga.InExecution);

        Assert.Equal(ResultadoCancelamento.EmAndamento, saga.SolicitarCancelamento("Cliente desistiu"));
        Assert.Equal(EstadoSaga.CancellingExecution, saga.Estado);
        Assert.Equal([ComandoEtapa.CancelamentoExecucao], saga.ComandosDaEtapa());

        Assert.True(saga.RegistrarCancelamentoExecucao(C("ExecutionCancelled.v1"), _execucao));
        Assert.Equal(EstadoSaga.Compensating, saga.Estado);
        Assert.Equal([ComandoEtapa.Estorno, ComandoEtapa.Liberacao], saga.ComandosDaEtapa());
    }

    [Fact]
    public void CancelamentoRecusadoEmReparo_VoltaAExecucaoSemEstorno()
    {
        var saga = Em(EstadoSaga.InExecution);
        saga.SolicitarCancelamento("Cliente desistiu");

        Assert.True(saga.RecusarCancelamentoExecucao(C("ExecutionCancelRejected.v1"), _execucao, "Em reparo"));

        Assert.Equal(EstadoSaga.InExecution, saga.Estado);
        Assert.False(saga.CancelamentoSolicitado);
        Assert.True(saga.ConcluirExecucao(C("ExecutionCompleted.v1"), _execucao));
    }

    [Fact]
    public void ConclusaoDuranteOPedidoDeCancelamento_Conclui()
    {
        var saga = Em(EstadoSaga.InExecution);
        saga.SolicitarCancelamento("Cliente desistiu");

        Assert.True(saga.ConcluirExecucao(C("ExecutionCompleted.v1"), _execucao));
        Assert.Equal(EstadoSaga.Completed, saga.Estado);
    }

    [Fact]
    public void CancelarEmAcaoManualOuTerminada_Recusado()
    {
        var saga = Em(EstadoSaga.AwaitingPayment);
        saga.AprovarPagamento(C("PaymentApproved.v1"), _pagamento, 1m, "BRL");

        Assert.Equal(ResultadoCancelamento.Recusado, saga.SolicitarCancelamento("x"));
        Assert.Equal(ResultadoCancelamento.Recusado, Em(EstadoSaga.Diagnosing).Also(s => s.SolicitarCancelamento("a")).SolicitarCancelamento("b"));
    }

    // ── Efeito depois do encerramento ──────────────────────────────────────────

    [Fact]
    public void ReservaOuPagamentoDepoisDoCancelamento_PedemDesfazer()
    {
        var saga = Em(EstadoSaga.ReservingInventory);
        saga.SolicitarCancelamento("x");
        saga.RejeitarReserva(C("InventoryReservationRejected.v1"), "Sem saldo");
        Assert.Equal(EstadoSaga.Cancelled, saga.Estado);

        Assert.True(saga.RegistrarReservaAposEncerramento(C("InventoryReserved.v1"), _reserva));
        Assert.False(saga.RegistrarReservaAposEncerramento(C("InventoryReserved.v1"), _reserva));
        Assert.True(saga.RegistrarPagamentoAposEncerramento(C("PaymentApproved.v1"), _pagamento));
        Assert.Equal(EstadoSaga.Cancelled, saga.Estado);
    }

    // ── Prazo e resolução pelo Admin ───────────────────────────────────────────

    [Fact]
    public void Prazo_ReenviaAteOLimiteEDepoisVaiParaAcaoManual()
    {
        var saga = Em(EstadoSaga.ReservingInventory);
        var prazo = TimeSpan.FromMinutes(5);
        var inicio = saga.EtapaIniciadaEm;

        Assert.False(saga.PrazoVencido(inicio.AddMinutes(4), prazo));
        Assert.True(saga.PrazoVencido(inicio.AddMinutes(5), prazo));
        saga.RegistrarReenvio();
        Assert.False(saga.PrazoVencido(inicio.AddMinutes(9), prazo));
        Assert.True(saga.PrazoVencido(inicio.AddMinutes(10), prazo));

        saga.EsgotarPrazo();
        Assert.Equal(EstadoSaga.ManualActionRequired, saga.Estado);
        Assert.Equal(CausaAcaoManual.PrazoEsgotado, saga.CausaManual);
        Assert.False(saga.PrazoVencido(inicio.AddDays(1), prazo));
    }

    [Theory]
    [InlineData(EstadoSaga.Diagnosing)]
    [InlineData(EstadoSaga.AwaitingApproval)]
    [InlineData(EstadoSaga.AwaitingPayment)]
    [InlineData(EstadoSaga.InExecution)]
    public void EstadosSemPrazo_NaoVencem(EstadoSaga estado)
        => Assert.False(Em(estado).PrazoVencido(DateTime.UtcNow.AddDays(1), TimeSpan.FromMinutes(5)));

    [Fact]
    public void Retomar_VoltaAEtapaComPrazoZerado()
    {
        var saga = Em(EstadoSaga.ReservingInventory);
        saga.EsgotarPrazo();

        saga.ResolverAcaoManual(AcaoResolucao.Retomar, "admin@oficina.example: Operações voltou");

        Assert.Equal(EstadoSaga.ReservingInventory, saga.Estado);
        Assert.Equal(0, saga.Reenvios);
        Assert.Null(saga.CausaManual);
        Assert.Equal([ComandoEtapa.Reserva], saga.ComandosDaEtapa());
        Assert.Equal("ResolucaoAdmin", saga.Transicoes.Last().Causa);
    }

    [Fact]
    public void PagamentoDivergente_SoPodeSerCanceladoComEstornoELiberacao()
    {
        var saga = Em(EstadoSaga.AwaitingPayment);
        saga.AprovarPagamento(C("PaymentApproved.v1"), _pagamento, 1m, "BRL");

        Assert.Throws<InvalidOperationException>(() => saga.ResolverAcaoManual(AcaoResolucao.Retomar, "tentar"));
        saga.ResolverAcaoManual(AcaoResolucao.Cancelar, "Valor errado");

        Assert.Equal(EstadoSaga.Compensating, saga.Estado);
        Assert.Equal([ComandoEtapa.Estorno, ComandoEtapa.Liberacao], saga.ComandosDaEtapa());
    }

    [Fact]
    public void CancelarAcaoManualSemEfeitos_Encerra()
    {
        var saga = Em(EstadoSaga.RequestingQuote);
        saga.EsgotarPrazo();

        saga.ResolverAcaoManual(AcaoResolucao.Cancelar, "Billing fora do ar");

        Assert.Equal(EstadoSaga.Cancelled, saga.Estado);
    }

    [Fact]
    public void ResolverSemMotivoOuForaDeAcaoManual_Recusado()
    {
        var saga = Em(EstadoSaga.RequestingQuote);
        Assert.Throws<InvalidOperationException>(() => saga.ResolverAcaoManual(AcaoResolucao.Cancelar, "x"));

        saga.EsgotarPrazo();
        Assert.Throws<ArgumentException>(() => saga.ResolverAcaoManual(AcaoResolucao.Cancelar, " "));
    }
}

internal static class TesteExtensoes
{
    public static T Also<T>(this T valor, Action<T> acao)
    {
        acao(valor);
        return valor;
    }
}
