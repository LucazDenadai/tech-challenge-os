using OficinaMecanica.OS.Domain.Entities;

namespace OficinaMecanica.OS.UnitTests.Dominio;

public class FilialTests
{
    [Fact]
    public void Construtor_NormalizaCodigo_ParaManterUnicidadeIndependenteDeCaixa()
    {
        var filial = new Filial("  filial-sp ", "Filial São Paulo");

        Assert.Equal("FILIAL-SP", filial.Codigo);
        Assert.True(filial.Ativo);
    }

    [Theory]
    [InlineData("", "Nome")]
    [InlineData("COD", " ")]
    public void Construtor_SemCodigoOuNome_LancaArgumentException(string codigo, string nome)
    {
        Assert.Throws<ArgumentException>(() => new Filial(codigo, nome));
    }

    [Fact]
    public void Desativar_MarcaInativa()
    {
        var filial = new Filial("COD", "Nome");

        filial.Desativar();

        Assert.False(filial.Ativo);
        Assert.NotNull(filial.AtualizadoEm);
    }
}
