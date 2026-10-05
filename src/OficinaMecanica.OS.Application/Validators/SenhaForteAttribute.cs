using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace OficinaMecanica.OS.Application.Validators;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class SenhaForteAttribute : ValidationAttribute
{
    private const int MinLength = 8;

    private static readonly Regex TemMaiuscula = new("[A-Z]",          RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex TemMinuscula = new("[a-z]",          RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex TemDigito    = new(@"\d",            RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex TemEspecial  = new(@"[^A-Za-z0-9]", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var senha = value as string;

        if (string.IsNullOrEmpty(senha))
            return ValidationResult.Success;

        var erros = new List<string>();

        if (senha.Length < MinLength)
            erros.Add($"mínimo de {MinLength} caracteres");
        if (!TemMaiuscula.IsMatch(senha))
            erros.Add("pelo menos uma letra maiúscula");
        if (!TemMinuscula.IsMatch(senha))
            erros.Add("pelo menos uma letra minúscula");
        if (!TemDigito.IsMatch(senha))
            erros.Add("pelo menos um número");
        if (!TemEspecial.IsMatch(senha))
            erros.Add("pelo menos um caractere especial");

        if (erros.Count == 0)
            return ValidationResult.Success;

        return new ValidationResult($"A senha deve conter: {string.Join(", ", erros)}.");
    }
}
