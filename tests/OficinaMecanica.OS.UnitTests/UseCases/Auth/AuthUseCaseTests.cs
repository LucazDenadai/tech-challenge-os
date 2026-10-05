using Moq;
using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Application.UseCases.Auth;
using OficinaMecanica.OS.Domain.Entities;
using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.UnitTests.UseCases.Auth;

public class AuthUseCaseTests
{
    private readonly Mock<IUsuarioRepository> _repoMock = new();
    private readonly Mock<ITokenService> _tokenMock = new();
    private readonly AuthUseCase _sut;

    public AuthUseCaseTests()
    {
        _sut = new AuthUseCase(_repoMock.Object, _tokenMock.Object);
    }

    [Fact]
    public async Task LoginAsync_CredenciaisCorretas_RetornaToken()
    {
        var usuario = new Usuario("Admin", "admin@email.com", "hash123", PerfilUsuario.Admin);
        _repoMock.Setup(r => r.ObterPorEmailAsync("admin@email.com", default)).ReturnsAsync(usuario);
        _tokenMock.Setup(t => t.VerificarSenha("senha123", "hash123")).Returns(true);
        _tokenMock.Setup(t => t.GerarToken(usuario.Id, usuario.Email, "Admin")).Returns("jwt-token");

        var result = await _sut.LoginAsync(new LoginRequest("admin@email.com", "senha123"));

        Assert.Equal("jwt-token", result);
    }

    [Fact]
    public async Task LoginAsync_SenhaIncorreta_LancaInvalidOperationException()
    {
        var usuario = new Usuario("Admin", "admin@email.com", "hash123", PerfilUsuario.Admin);
        _repoMock.Setup(r => r.ObterPorEmailAsync("admin@email.com", default)).ReturnsAsync(usuario);
        _tokenMock.Setup(t => t.VerificarSenha("errada", "hash123")).Returns(false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.LoginAsync(new LoginRequest("admin@email.com", "errada")));
    }

    [Fact]
    public async Task LoginAsync_UsuarioNaoEncontrado_LancaNotFoundException()
    {
        _repoMock.Setup(r => r.ObterPorEmailAsync("nao@existe.com", default)).ReturnsAsync((Usuario?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.LoginAsync(new LoginRequest("nao@existe.com", "senha")));
    }
}
