using Moq;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Application.UseCases.Cliente;
using DomainCliente = OficinaMecanica.OS.Domain.Entities.Cliente;

namespace OficinaMecanica.OS.UnitTests.UseCases.Cliente;

public class BuscarClientePorCpfUseCaseTests
{
    private readonly Mock<IClienteRepository> _repoMock = new();
    private readonly BuscarClientePorCpfUseCase _sut;

    public BuscarClientePorCpfUseCaseTests()
    {
        _sut = new BuscarClientePorCpfUseCase(_repoMock.Object);
    }

    [Fact]
    public async Task ExecutarAsync_CpfFormatado_BuscaPeloDocumentoSanitizado()
    {
        var cliente = new DomainCliente("Cliente Demo", "11144477735", "c@cliente.example", "11900000000", "Rua Demo");
        _repoMock.Setup(r => r.ObterPorDocumentoAsync("11144477735", default)).ReturnsAsync(cliente);

        var resultado = await _sut.ExecutarAsync("111.444.777-35");

        Assert.Equal(new ClienteAutenticacaoResponse(cliente.Id, true), resultado);
    }

    [Fact]
    public async Task ExecutarAsync_ClienteDesativado_DevolveAtivoFalso()
    {
        var cliente = new DomainCliente("Cliente Demo", "11144477735", "c@cliente.example", "11900000000", "Rua Demo");
        cliente.Desativar();
        _repoMock.Setup(r => r.ObterPorDocumentoAsync("11144477735", default)).ReturnsAsync(cliente);

        var resultado = await _sut.ExecutarAsync("11144477735");

        Assert.False(resultado!.Ativo);
    }

    [Fact]
    public async Task ExecutarAsync_CpfSemCadastro_DevolveNulo()
    {
        _repoMock.Setup(r => r.ObterPorDocumentoAsync("52998224725", default)).ReturnsAsync((DomainCliente?)null);

        Assert.Null(await _sut.ExecutarAsync("52998224725"));
    }

    [Theory]
    [InlineData("12345678900")]
    [InlineData("111")]
    [InlineData("")]
    public async Task ExecutarAsync_CpfInvalido_LancaArgumentExceptionSemConsultarBanco(string cpf)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.ExecutarAsync(cpf));
        _repoMock.VerifyNoOtherCalls();
    }
}
