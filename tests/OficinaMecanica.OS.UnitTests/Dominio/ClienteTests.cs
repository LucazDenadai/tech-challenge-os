using DomainCliente = OficinaMecanica.OS.Domain.Entities.Cliente;

namespace OficinaMecanica.OS.UnitTests.Dominio;

// O e-mail é obrigatório: é o payerEmail do Pix pedido pela Saga (ADR-019, CARD-40a).
public class ClienteTests
{
    private static DomainCliente Criar(string email) => new("Cliente", "11144477735", email, "11900000000", "Rua A");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sem-arroba")]
    [InlineData("a@b")]
    [InlineData("dois@@exemplo.com")]
    public void Construtor_EmailInvalido_LancaArgumentException(string email)
        => Assert.Throws<ArgumentException>(() => Criar(email));

    [Fact]
    public void Construtor_EmailValido_GuardaSemEspacos()
        => Assert.Equal("cliente@exemplo.com", Criar("  cliente@exemplo.com ").Email);

    [Fact]
    public void Atualizar_EmailVazio_LancaArgumentExceptionSemMudarCadastro()
    {
        var cliente = Criar("cliente@exemplo.com");

        Assert.Throws<ArgumentException>(() => cliente.Atualizar("Outro", "", "11", "Rua B"));

        Assert.Equal("cliente@exemplo.com", cliente.Email);
    }

    [Fact]
    public void Ativar_EmailInvalido_LancaArgumentException()
    {
        var cliente = Criar("cliente@exemplo.com");
        cliente.Desativar();

        Assert.Throws<ArgumentException>(() => cliente.Ativar("Cliente", "invalido", "11", "Rua A"));
    }
}
