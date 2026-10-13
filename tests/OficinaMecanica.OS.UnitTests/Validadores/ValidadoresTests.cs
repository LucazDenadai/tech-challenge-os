using System.ComponentModel.DataAnnotations;
using OficinaMecanica.OS.Application.Validators;
using OficinaMecanica.OS.Domain.Validators;

namespace OficinaMecanica.OS.UnitTests.Validadores;

// Regras de entrada: senha forte dos funcionários e documentos/placa do cadastro.
public class ValidadoresTests
{
    private sealed class ComSenha
    {
        [SenhaForte] public string? Senha { get; set; }
    }

    private static IList<ValidationResult> Validar(string? senha)
    {
        var modelo = new ComSenha { Senha = senha };
        var erros = new List<ValidationResult>();
        Validator.TryValidateObject(modelo, new ValidationContext(modelo), erros, validateAllProperties: true);
        return erros;
    }

    [Theory]
    [InlineData("Senha@Forte1")]
    [InlineData(null)]
    [InlineData("")]
    public void SenhaForte_ValidaOuAusente_Aceita(string? senha) => Assert.Empty(Validar(senha));

    [Theory]
    [InlineData("Ab@1", "mínimo de 8 caracteres")]
    [InlineData("senha@forte1", "letra maiúscula")]
    [InlineData("SENHA@FORTE1", "letra minúscula")]
    [InlineData("Senha@Forte", "número")]
    [InlineData("SenhaForte1", "caractere especial")]
    public void SenhaForte_SemUmCriterio_RecusaDizendoQual(string senha, string criterio)
    {
        var erro = Assert.Single(Validar(senha));
        Assert.Contains(criterio, erro.ErrorMessage);
    }

    [Fact]
    public void SenhaForte_VariosCriteriosFaltando_ListaTodos()
    {
        var erro = Assert.Single(Validar("abc"));
        Assert.Contains("mínimo de 8 caracteres", erro.ErrorMessage);
        Assert.Contains("letra maiúscula", erro.ErrorMessage);
        Assert.Contains("número", erro.ErrorMessage);
        Assert.Contains("caractere especial", erro.ErrorMessage);
    }

    [Theory]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("11222333000181", true)]
    [InlineData("11222333000182", false)]   // segundo dígito errado
    [InlineData("11222333000191", false)]   // primeiro dígito errado
    [InlineData("11111111111111", false)]   // todos iguais
    [InlineData("1122233300018", false)]    // 13 dígitos
    [InlineData(null, false)]
    public void Cnpj(string? cnpj, bool valido)
    {
        Assert.Equal(valido, CnpjValidator.Valido(cnpj!));
        if (!valido)
            Assert.Throws<ArgumentException>(() => CnpjValidator.Validar(cnpj!));
    }

    [Theory]
    [InlineData("111.444.777-35", true)]
    [InlineData("11144477736", false)]      // segundo dígito errado
    [InlineData("11144477725", false)]      // primeiro dígito errado
    [InlineData("00000000000", false)]
    [InlineData("1114447773", false)]
    public void Cpf(string cpf, bool valido)
    {
        if (valido)
            CpfValidator.Validar(cpf);
        else
            Assert.ThrowsAny<ArgumentException>(() => CpfValidator.Validar(cpf));
    }

    [Theory]
    [InlineData("ABC1D23", true)]   // Mercosul
    [InlineData("ABC-1234", true)]  // antiga
    [InlineData("AB1234", false)]
    [InlineData("", false)]
    public void Placa(string placa, bool valida)
    {
        if (valida)
            PlacaValidator.Validar(placa);
        else
            Assert.ThrowsAny<ArgumentException>(() => PlacaValidator.Validar(placa));
    }
}
