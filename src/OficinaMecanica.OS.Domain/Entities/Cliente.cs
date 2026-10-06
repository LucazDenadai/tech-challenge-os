using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using OficinaMecanica.OS.Domain.Validators;

namespace OficinaMecanica.OS.Domain.Entities;

public class Cliente : EntityBase
{
    public string Nome { get; private set; } = string.Empty;
    public string Documento { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Telefone { get; private set; } = string.Empty;
    public string Endereco { get; private set; } = string.Empty;
    public bool Ativo { get; private set; } = true;

    private readonly List<Veiculo> _veiculos = new();
    [ExcludeFromCodeCoverage]
    public IReadOnlyCollection<Veiculo> Veiculos => _veiculos.AsReadOnly();

    [ExcludeFromCodeCoverage]
    protected Cliente() { }

    public Cliente(string nome, string documento, string email, string telefone, string endereco)
    {
        var documentoSanitizado = Sanitizar(documento);
        ValidarDocumento(documentoSanitizado);
        Nome = nome;
        Documento = documentoSanitizado;
        Email = email;
        Telefone = telefone;
        Endereco = endereco;
    }

    public static string Sanitizar(string documento)
        => Regex.Replace((documento ?? "").ToUpperInvariant(), @"[.\-/\s]", "", RegexOptions.None, TimeSpan.FromMilliseconds(100));

    public void Atualizar(string nome, string email, string telefone, string endereco)
    {
        Nome = nome;
        Email = email;
        Telefone = telefone;
        Endereco = endereco;
        MarcarAtualizado();
    }

    public void Ativar(string nome, string email, string telefone, string endereco)
    {
        Nome = nome;
        Email = email;
        Telefone = telefone;
        Endereco = endereco;
        Ativo = true;
        MarcarAtualizado();
    }

    public void Desativar()
    {
        Ativo = false;
        MarcarAtualizado();
    }

    private static void ValidarDocumento(string documento)
    {
        if (documento.Length == 11)
            CpfValidator.Validar(documento);
        else if (documento.Length == 14)
            CnpjValidator.Validar(documento);
        else
            throw new ArgumentException("Documento inválido. Informe um CPF (11 dígitos) ou CNPJ (14 dígitos).", nameof(documento));
    }
}
