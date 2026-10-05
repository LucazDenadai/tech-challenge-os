using Moq;
using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Application.UseCases.OrdemServico;
using OficinaMecanica.OS.Domain.Enums;
using DomainOS = OficinaMecanica.OS.Domain.Entities.OrdemServico;

namespace OficinaMecanica.OS.UnitTests.UseCases.OS;

public class ConsultarStatusOSUseCaseTests
{
    private readonly Mock<IOrdemServicoRepository> _repoMock = new();
    private readonly ConsultarStatusOSUseCase _sut;

    public ConsultarStatusOSUseCaseTests()
    {
        _sut = new ConsultarStatusOSUseCase(_repoMock.Object);
    }

    [Fact]
    public async Task ExecutarAsync_OSExiste_RetornaStatusEHistoricoEmOrdem()
    {
        var os = new DomainOS("OS-001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Obs");
        os.AlterarStatus(StatusOrdemServico.AguardandoAprovacao);
        os.AlterarStatus(StatusOrdemServico.AguardandoPagamento);
        _repoMock.Setup(r => r.ObterComDetalhesAsync(os.Id, default)).ReturnsAsync(os);

        var result = await _sut.ExecutarAsync(os.Id);

        Assert.Equal(os.Id, result.Id);
        Assert.Equal("OS-001", result.Numero);
        Assert.Equal(StatusOrdemServico.AguardandoPagamento, result.Status);
        Assert.Collection(result.Historico,
            h => Assert.Equal((StatusOrdemServico.EmDiagnostico, StatusOrdemServico.AguardandoAprovacao), (h.StatusAnterior, h.StatusNovo)),
            h => Assert.Equal((StatusOrdemServico.AguardandoAprovacao, StatusOrdemServico.AguardandoPagamento), (h.StatusAnterior, h.StatusNovo)));
    }

    [Fact]
    public async Task ExecutarAsync_OSNaoEncontrada_LancaNotFoundException()
    {
        var id = Guid.NewGuid();
        _repoMock.Setup(r => r.ObterComDetalhesAsync(id, default)).ReturnsAsync((DomainOS?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ExecutarAsync(id));
    }
}
