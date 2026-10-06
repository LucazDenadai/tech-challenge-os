using System.Diagnostics.CodeAnalysis;
using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Domain.Entities;

public class Usuario : EntityBase
{
    public string Nome { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string SenhaHash { get; private set; } = string.Empty;
    public PerfilUsuario Perfil { get; private set; }
    public bool Ativo { get; private set; } = true;

    [ExcludeFromCodeCoverage]
    protected Usuario() { }

    public Usuario(string nome, string email, string senhaHash, PerfilUsuario perfil)
    {
        Nome = nome;
        Email = email;
        SenhaHash = senhaHash;
        Perfil = perfil;
    }

    public void Atualizar(string nome, string email, PerfilUsuario perfil)
    {
        Nome = nome;
        Email = email;
        Perfil = perfil;
        MarcarAtualizado();
    }

    public void AlterarSenha(string novaSenhaHash)
    {
        SenhaHash = novaSenhaHash;
        MarcarAtualizado();
    }

    public void Desativar()
    {
        Ativo = false;
        MarcarAtualizado();
    }
}
