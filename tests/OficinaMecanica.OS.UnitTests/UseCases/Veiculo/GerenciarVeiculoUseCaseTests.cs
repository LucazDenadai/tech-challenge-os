using Moq;
using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Application.UseCases.Veiculo;
using DomainCliente = OficinaMecanica.OS.Domain.Entities.Cliente;
using DomainVeiculo = OficinaMecanica.OS.Domain.Entities.Veiculo;

namespace OficinaMecanica.OS.UnitTests.UseCases.Veiculos;

public class GerenciarVeiculoUseCaseTests
{
    private readonly Mock<IVeiculoRepository> _veiculoRepoMock = new();
    private readonly Mock<IClienteRepository> _clienteRepoMock = new();
    private readonly GerenciarVeiculoUseCase _sut;

    public GerenciarVeiculoUseCaseTests()
    {
        _sut = new GerenciarVeiculoUseCase(_veiculoRepoMock.Object, _clienteRepoMock.Object);
    }

    [Fact]
    public async Task CriarAsync_PlacaValidaClienteExiste_RetornaId()
    {
        var clienteId = Guid.NewGuid();
        var cliente = new DomainCliente("João", "12345678909", "j@email.com", "11999", "Rua A");
        _clienteRepoMock.Setup(r => r.ObterPorIdAsync(clienteId, default)).ReturnsAsync(cliente);
        _veiculoRepoMock.Setup(r => r.ObterPorPlacaAsync("ABC1D23", default)).ReturnsAsync((DomainVeiculo?)null);

        var request = new CriarVeiculoRequest(clienteId, "ABC1D23", "Ford", "Fiesta", 2020, "Prata");
        var id = await _sut.CriarAsync(request);

        Assert.NotEqual(Guid.Empty, id);
        _veiculoRepoMock.Verify(r => r.AdicionarAsync(It.IsAny<DomainVeiculo>(), default), Times.Once);
    }

    [Fact]
    public async Task CriarAsync_PlacaDuplicada_LancaInvalidOperationException()
    {
        var clienteId = Guid.NewGuid();
        var cliente = new DomainCliente("João", "12345678909", "j@email.com", "11999", "Rua A");
        _clienteRepoMock.Setup(r => r.ObterPorIdAsync(clienteId, default)).ReturnsAsync(cliente);
        var veiculoExistente = new DomainVeiculo(Guid.NewGuid(), "ABC1D23", "Ford", "Fiesta", 2020, "Prata");
        _veiculoRepoMock.Setup(r => r.ObterPorPlacaAsync("ABC1D23", default)).ReturnsAsync(veiculoExistente);

        var request = new CriarVeiculoRequest(clienteId, "ABC1D23", "VW", "Gol", 2019, "Branco");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CriarAsync(request));
    }

    [Fact]
    public async Task CriarAsync_ClienteNaoExiste_LancaNotFoundException()
    {
        var clienteId = Guid.NewGuid();
        _clienteRepoMock.Setup(r => r.ObterPorIdAsync(clienteId, default)).ReturnsAsync((DomainCliente?)null);

        var request = new CriarVeiculoRequest(clienteId, "ABC1D23", "Ford", "Fiesta", 2020, "Prata");

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CriarAsync(request));
    }
}
