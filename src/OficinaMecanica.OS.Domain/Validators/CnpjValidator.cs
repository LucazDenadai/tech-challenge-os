using System.Text.RegularExpressions;

namespace OficinaMecanica.OS.Domain.Validators;

public static class CnpjValidator
{
    public static void Validar(string cnpj)
    {
        if (!Valido(cnpj))
            throw new ArgumentException("CNPJ inválido.", nameof(cnpj));
    }

    public static bool Valido(string cnpj)
    {
        var chars = Regex.Replace((cnpj ?? "").ToUpperInvariant(), @"[.\-/\s]", "", RegexOptions.None, TimeSpan.FromMilliseconds(100));

        if (chars.Length != 14)
            return false;

        if (chars.Distinct().Count() == 1)
            return false;

        return CalcularDigito(chars, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) == (chars[12] - '0')
            && CalcularDigito(chars, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) == (chars[13] - '0');
    }

    private static int CalcularDigito(string chars, int[] pesos)
    {
        var soma = pesos.Select((p, i) => (chars[i] - 48) * p).Sum();
        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
