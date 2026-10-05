using Moq;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Application.UseCases.OrdemServico;
using OficinaMecanica.OS.Domain.Enums;
using DomainOS = OficinaMecanica.OS.Domain.Entities.OrdemServico;

namespace OficinaMecanica.OS.UnitTests.UseCases.OS;

public class ListarOrdensServicoUseCaseTests
{
    private readonly Mock<IOrdemServicoRepository> _repoMock = new();
    private readonly ListarOrdensServicoUseCase _sut;

    public ListarOrdensServicoUseCaseTests()
    {
        _sut = new ListarOrdensServicoUseCase(_repoMock.Object);
    }

    private static DomainOS CriarOSComStatus(string numero, StatusOrdemServico status)
    {
        var os = new DomainOS(numero, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "");
        if (status == StatusOrdemServico.Cancelada)
        {
            os.AlterarStatus(StatusOrdemServico.Cancelada);
            return os;
        }
        var sequencia = new[]
        {
            StatusOrdemServico.AguardandoAprovacao,
            StatusOrdemServico.AguardandoPagamento,
            StatusOrdemServico.EmExecucao,
            StatusOrdemServico.Finalizada,
            StatusOrdemServico.Entregue,
        };
        foreach (var s in sequencia)
        {
            if (os.Status == status) break;
            os.AlterarStatus(s);
        }
        return os;
    }

    [Fact]
    public async Task ExecutarAsync_OrdenaPorProximidadeDaEntrega()
    {
        var osDiagnostico = CriarOSComStatus("OS-001", StatusOrdemServico.EmDiagnostico);
        var osAprovacao = CriarOSComStatus("OS-002", StatusOrdemServico.AguardandoAprovacao);
        var osExecucao = CriarOSComStatus("OS-003", StatusOrdemServico.EmExecucao);
        var osPagamento = CriarOSComStatus("OS-004", StatusOrdemServico.AguardandoPagamento);

        _repoMock.Setup(r => r.ObterTodosAsync(default))
            .ReturnsAsync([osDiagnostico, osAprovacao, osExecucao, osPagamento]);

        var result = (await _sut.ExecutarAsync()).Select(r => r.Status).ToList();

        Assert.Equal(
        [
            StatusOrdemServico.EmExecucao,
            StatusOrdemServico.AguardandoPagamento,
            StatusOrdemServico.AguardandoAprovacao,
            StatusOrdemServico.EmDiagnostico
        ], result);
    }

    [Theory]
    [InlineData(StatusOrdemServico.Finalizada)]
    [InlineData(StatusOrdemServico.Entregue)]
    [InlineData(StatusOrdemServico.Cancelada)]
    public async Task ExecutarAsync_NaoRetornaOSEncerrada(StatusOrdemServico status)
    {
        _repoMock.Setup(r => r.ObterTodosAsync(default)).ReturnsAsync([CriarOSComStatus("OS-001", status)]);

        var result = await _sut.ExecutarAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task ExecutarAsync_MesmoStatus_OrdenaPorDataAberturaMaisAntiga()
    {
        var os1 = CriarOSComStatus("OS-001", StatusOrdemServico.EmDiagnostico);
        await Task.Delay(10);
        var os2 = CriarOSComStatus("OS-002", StatusOrdemServico.EmDiagnostico);

        _repoMock.Setup(r => r.ObterTodosAsync(default)).ReturnsAsync([os2, os1]);

        var result = (await _sut.ExecutarAsync()).ToList();

        Assert.Equal("OS-001", result[0].Numero);
        Assert.Equal("OS-002", result[1].Numero);
    }
}
