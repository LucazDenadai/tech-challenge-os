using Moq;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Application.UseCases.OrdemServico;
using OficinaMecanica.OS.Domain.Enums;
using DomainOS = OficinaMecanica.OS.Domain.Entities.OrdemServico;

namespace OficinaMecanica.OS.UnitTests.UseCases.OrdemServico;

public class AcompanharOSUseCaseTests
{
    private readonly Mock<IOrdemServicoRepository> _repoMock = new();
    private readonly AcompanharOSUseCase _sut;
    private readonly Guid _clienteId = Guid.NewGuid();

    public AcompanharOSUseCaseTests()
    {
        _sut = new AcompanharOSUseCase(_repoMock.Object);
    }

    [Fact]
    public async Task ExecutarAsync_OSDoProprioCliente_DevolveStatusEHistorico()
    {
        var os = new DomainOS("OS-2026-0001", _clienteId, Guid.NewGuid(), Guid.NewGuid(), "");
        os.AlterarStatus(StatusOrdemServico.AguardandoAprovacao);
        _repoMock.Setup(r => r.ObterPorNumeroAsync("OS-2026-0001", default)).ReturnsAsync(os);

        var resultado = await _sut.ExecutarAsync("OS-2026-0001", _clienteId);

        Assert.Equal(StatusOrdemServico.AguardandoAprovacao, resultado!.Status);
        Assert.Single(resultado.Historico);
    }

    [Fact]
    public async Task ExecutarAsync_OSDeOutroCliente_DevolveNulo()
    {
        var os = new DomainOS("OS-2026-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "");
        _repoMock.Setup(r => r.ObterPorNumeroAsync("OS-2026-0001", default)).ReturnsAsync(os);

        Assert.Null(await _sut.ExecutarAsync("OS-2026-0001", _clienteId));
    }

    [Fact]
    public async Task ExecutarAsync_NumeroInexistente_DevolveNulo()
    {
        _repoMock.Setup(r => r.ObterPorNumeroAsync("OS-2026-9999", default)).ReturnsAsync((DomainOS?)null);

        Assert.Null(await _sut.ExecutarAsync("OS-2026-9999", _clienteId));
    }
}
