using OficinaMecanica.OS.Domain.Enums;
using DomainOS = OficinaMecanica.OS.Domain.Entities.OrdemServico;

namespace OficinaMecanica.OS.UnitTests.Dominio;

public class OrdemServicoTests
{
    private static readonly StatusOrdemServico[] _fluxoNormal =
    [
        StatusOrdemServico.AguardandoAprovacao,
        StatusOrdemServico.AguardandoPagamento,
        StatusOrdemServico.EmExecucao,
        StatusOrdemServico.Finalizada,
        StatusOrdemServico.Entregue
    ];

    private static DomainOS CriarOS() => new("OS-001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Obs");

    private static DomainOS CriarOSEm(StatusOrdemServico status)
    {
        var os = CriarOS();
        foreach (var s in _fluxoNormal)
        {
            if (os.Status == status) break;
            os.AlterarStatus(s);
        }
        return os;
    }

    [Fact]
    public void Construtor_NasceEmDiagnostico_PorqueAberturaSolicitaDiagnostico()
    {
        // ADR-017: a OS nasce com a Saga em Diagnosing; o status "Recebida" da Fase 3 não existe mais.
        var os = CriarOS();

        Assert.Equal(StatusOrdemServico.EmDiagnostico, os.Status);
        Assert.Empty(os.Historico);
    }

    [Fact]
    public void Construtor_SemFilial_LancaArgumentException()
    {
        // ADR-015: toda OS é associada a uma filial; sem ela Operações não sabe de qual estoque reservar.
        Assert.Throws<ArgumentException>(() => new DomainOS("OS-001", Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "Obs"));
    }

    [Fact]
    public void AlterarStatus_FluxoCompleto_PassaPorAguardandoPagamentoAntesDaExecucao()
    {
        // ADR-017: a execução só começa após o pagamento aprovado.
        var os = CriarOS();

        foreach (var s in _fluxoNormal)
            os.AlterarStatus(s);

        Assert.Equal(StatusOrdemServico.Entregue, os.Status);
        Assert.Equal(_fluxoNormal, os.Historico.Select(h => h.StatusNovo));
        Assert.NotNull(os.DataFechamento);
    }

    [Fact]
    public void AlterarStatus_AprovacaoDiretoParaExecucao_RejeitaPularPagamento()
    {
        var os = CriarOSEm(StatusOrdemServico.AguardandoAprovacao);

        var ex = Assert.Throws<InvalidOperationException>(() => os.AlterarStatus(StatusOrdemServico.EmExecucao));

        Assert.Contains(nameof(StatusOrdemServico.AguardandoPagamento), ex.Message);
        Assert.Equal(StatusOrdemServico.AguardandoAprovacao, os.Status);
    }

    [Fact]
    public void AlterarStatus_TransicaoInvalida_NaoAlteraEstadoNemHistorico()
    {
        var os = CriarOSEm(StatusOrdemServico.AguardandoPagamento);
        var historicoAntes = os.Historico.Count;
        var atualizadoAntes = os.AtualizadoEm;

        Assert.Throws<InvalidOperationException>(() => os.AlterarStatus(StatusOrdemServico.Finalizada));

        Assert.Equal(StatusOrdemServico.AguardandoPagamento, os.Status);
        Assert.Equal(historicoAntes, os.Historico.Count);
        Assert.Equal(atualizadoAntes, os.AtualizadoEm);
    }

    [Theory]
    [InlineData(StatusOrdemServico.EmDiagnostico)]
    [InlineData(StatusOrdemServico.AguardandoAprovacao)]
    [InlineData(StatusOrdemServico.AguardandoPagamento)]
    [InlineData(StatusOrdemServico.EmExecucao)]
    public void AlterarStatus_CancelarEmEstadoDaSaga_RegistraCancelamento(StatusOrdemServico origem)
    {
        // ADR-017: Cancelled/Compensated podem ocorrer em qualquer etapa da Saga até a execução.
        var os = CriarOSEm(origem);

        os.AlterarStatus(StatusOrdemServico.Cancelada);

        Assert.Equal(StatusOrdemServico.Cancelada, os.Status);
        Assert.NotNull(os.DataFechamento);
        var ultimo = os.Historico.Last();
        Assert.Equal(origem, ultimo.StatusAnterior);
        Assert.Equal(StatusOrdemServico.Cancelada, ultimo.StatusNovo);
    }

    [Fact]
    public void AlterarStatus_CancelarFinalizada_Rejeita()
    {
        // Após Completed a Saga terminou; Finalizada só segue para Entregue (transição manual do Atendente).
        var os = CriarOSEm(StatusOrdemServico.Finalizada);

        Assert.Throws<InvalidOperationException>(() => os.AlterarStatus(StatusOrdemServico.Cancelada));
        Assert.Equal(StatusOrdemServico.Finalizada, os.Status);
    }

    [Theory]
    [InlineData(StatusOrdemServico.Entregue)]
    [InlineData(StatusOrdemServico.Cancelada)]
    public void AlterarStatus_EstadoTerminal_NaoPermiteNenhumaTransicao(StatusOrdemServico terminal)
    {
        var os = terminal == StatusOrdemServico.Cancelada ? CriarOS() : CriarOSEm(terminal);
        if (terminal == StatusOrdemServico.Cancelada) os.AlterarStatus(StatusOrdemServico.Cancelada);
        var historicoAntes = os.Historico.Count;

        foreach (var s in Enum.GetValues<StatusOrdemServico>())
            Assert.Throws<InvalidOperationException>(() => os.AlterarStatus(s));

        Assert.Equal(terminal, os.Status);
        Assert.Equal(historicoAntes, os.Historico.Count);
    }
}
