using System.Text.RegularExpressions;

namespace OficinaMecanica.OS.Domain.Validators;

public static class CpfValidator
{
    public static void Validar(string cpf)
    {
        if (!Valido(cpf))
            throw new ArgumentException("CPF inválido.", nameof(cpf));
    }

    public static bool Valido(string cpf)
    {
        var digits = Regex.Replace(cpf ?? "", @"\D", "", RegexOptions.None, TimeSpan.FromMilliseconds(100));

        if (digits.Length != 11)
            return false;

        if (digits.Distinct().Count() == 1)
            return false;

        return CalcularDigito(digits, 10) == int.Parse(digits[9].ToString())
            && CalcularDigito(digits, 11) == int.Parse(digits[10].ToString());
    }

    private static int CalcularDigito(string digits, int peso)
    {
        var soma = 0;
        for (var i = 0; i < peso - 1; i++)
            soma += int.Parse(digits[i].ToString()) * (peso - i);

        var resto = (soma * 10) % 11;
        return resto == 10 ? 0 : resto;
    }
}
