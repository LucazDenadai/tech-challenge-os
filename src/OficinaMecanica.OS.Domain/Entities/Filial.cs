using System.Diagnostics.CodeAnalysis;

namespace OficinaMecanica.OS.Domain.Entities;

public class Filial : EntityBase
{
    public string Codigo { get; private set; } = string.Empty;
    public string Nome { get; private set; } = string.Empty;
    public bool Ativo { get; private set; } = true;

    [ExcludeFromCodeCoverage]
    protected Filial() { }

    public Filial(string codigo, string nome)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw new ArgumentException("O código da filial é obrigatório.", nameof(codigo));
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome da filial é obrigatório.", nameof(nome));

        Codigo = codigo.Trim().ToUpperInvariant();
        Nome = nome;
    }

    public void Desativar()
    {
        Ativo = false;
        MarcarAtualizado();
    }
}
