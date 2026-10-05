using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Application.UseCases.OrdemServico;
using OficinaMecanica.OS.Domain.Entities;
using OficinaMecanica.OS.Domain.Enums;
using DomainOS = OficinaMecanica.OS.Domain.Entities.OrdemServico;
using DomainVeiculo = OficinaMecanica.OS.Domain.Entities.Veiculo;

namespace OficinaMecanica.OS.UnitTests.UseCases.OS;

public class AbrirOrdemServicoUseCaseTests
{
    private readonly Mock<IOrdemServicoRepository> _osRepoMock = new();
    private readonly Mock<IVeiculoRepository> _veiculoRepoMock = new();
    private readonly Mock<IFilialRepository> _filialRepoMock = new();
    private readonly AbrirOrdemServicoUseCase _sut;

    private readonly Guid _clienteId = Guid.NewGuid();
    private readonly Guid _veiculoId = Guid.NewGuid();
    private readonly Filial _filial = new("FILIAL-SP", "Filial SP");

    public AbrirOrdemServicoUseCaseTests()
    {
        _sut = new AbrirOrdemServicoUseCase(_osRepoMock.Object, _veiculoRepoMock.Object, _filialRepoMock.Object, NullLogger<AbrirOrdemServicoUseCase>.Instance);
        _veiculoRepoMock.Setup(r => r.ObterPorIdAsync(_veiculoId, default))
            .ReturnsAsync(new DomainVeiculo(_clienteId, "ABC1D23", "Ford", "Fiesta", 2020, "Prata"));
        _filialRepoMock.Setup(r => r.ObterPorIdAsync(_filial.Id, default)).ReturnsAsync(_filial);
        _osRepoMock.Setup(r => r.GerarNumeroAsync(default)).ReturnsAsync("OS-001");
    }

    [Fact]
    public async Task ExecutarAsync_DadosValidos_CriaOSEmDiagnosticoNaFilial()
    {
        var result = await _sut.ExecutarAsync(new AbrirOrdemServicoRequest(_clienteId, _veiculoId, _filial.Id, "Revisão"));

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("OS-001", result.Numero);
        _osRepoMock.Verify(r => r.AdicionarAsync(
            It.Is<DomainOS>(os => os.Status == StatusOrdemServico.EmDiagnostico && os.FilialId == _filial.Id),
            default), Times.Once);
        _osRepoMock.Verify(r => r.SalvarAsync(default), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_VeiculoDeOutroCliente_LancaInvalidOperationException()
    {
        var request = new AbrirOrdemServicoRequest(Guid.NewGuid(), _veiculoId, _filial.Id, "Obs");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ExecutarAsync(request));
        _osRepoMock.Verify(r => r.SalvarAsync(default), Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_FilialInexistente_LancaNotFoundExceptionSemPersistir()
    {
        var request = new AbrirOrdemServicoRequest(_clienteId, _veiculoId, Guid.NewGuid(), "Obs");

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ExecutarAsync(request));
        _osRepoMock.Verify(r => r.SalvarAsync(default), Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_FilialInativa_LancaInvalidOperationExceptionSemPersistir()
    {
        _filial.Desativar();
        var request = new AbrirOrdemServicoRequest(_clienteId, _veiculoId, _filial.Id, "Obs");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ExecutarAsync(request));
        _osRepoMock.Verify(r => r.SalvarAsync(default), Times.Never);
    }
}
