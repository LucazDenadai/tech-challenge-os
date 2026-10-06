using Moq;
using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Application.UseCases.Cliente;
using OficinaMecanica.OS.Domain.Entities;

namespace OficinaMecanica.OS.UnitTests.UseCases.Cliente;

public class GerenciarClienteUseCaseTests
{
    private readonly Mock<IClienteRepository> _repoMock = new();
    private readonly GerenciarClienteUseCase _sut;

    public GerenciarClienteUseCaseTests()
    {
        _sut = new GerenciarClienteUseCase(_repoMock.Object);
    }

    [Fact]
    public async Task CriarAsync_CPFValido_RetornaId()
    {
        _repoMock.Setup(r => r.DocumentoExisteAsync("12345678909", null, default)).ReturnsAsync(false);

        var request = new CriarClienteRequest("João", "123.456.789-09", "j@email.com", "11999", "Rua A");
        var id = await _sut.CriarAsync(request);

        Assert.NotEqual(Guid.Empty, id);
        _repoMock.Verify(r => r.AdicionarAsync(It.IsAny<Domain.Entities.Cliente>(), default), Times.Once);
    }

    [Fact]
    public async Task CriarAsync_CNPJValido_RetornaId()
    {
        _repoMock.Setup(r => r.DocumentoExisteAsync("11222333000181", null, default)).ReturnsAsync(false);

        var request = new CriarClienteRequest("Empresa", "11.222.333/0001-81", "emp@email.com", "1133334444", "Av B");
        var id = await _sut.CriarAsync(request);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task CriarAsync_DocumentoDuplicadoAtivo_LancaInvalidOperationException()
    {
        _repoMock.Setup(r => r.DocumentoExisteAsync("12345678909", null, default)).ReturnsAsync(true);
        _repoMock.Setup(r => r.ObterPorDocumentoAsync("12345678909", default))
            .ReturnsAsync(new Domain.Entities.Cliente("João", "12345678909", "j@email.com", "11999", "Rua A"));

        var request = new CriarClienteRequest("João2", "123.456.789-09", "j2@email.com", "11998", "Rua B");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CriarAsync(request));
    }

    [Fact]
    public async Task DesativarAsync_ClienteExiste_MarcaInativo()
    {
        var cliente = new Domain.Entities.Cliente("João", "12345678909", "j@email.com", "11999", "Rua A");
        _repoMock.Setup(r => r.ObterPorIdAsync(cliente.Id, default)).ReturnsAsync(cliente);

        await _sut.DesativarAsync(cliente.Id);

        Assert.False(cliente.Ativo);
        _repoMock.Verify(r => r.SalvarAsync(default), Times.Once);
    }

    [Fact]
    public async Task DesativarAsync_ClienteNaoEncontrado_LancaNotFoundException()
    {
        var id = Guid.NewGuid();
        _repoMock.Setup(r => r.ObterPorIdAsync(id, default)).ReturnsAsync((Domain.Entities.Cliente?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DesativarAsync(id));
    }
}
