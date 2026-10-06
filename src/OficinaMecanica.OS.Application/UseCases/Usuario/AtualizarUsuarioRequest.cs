using System.ComponentModel.DataAnnotations;
using OficinaMecanica.OS.Application.Validators;
using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Application.UseCases.Usuario;

public class AtualizarUsuarioRequest
{
    [Required(ErrorMessage = "O nome é obrigatório.")]
    [MaxLength(100, ErrorMessage = "O nome deve ter no máximo 100 caracteres.")]
    public string Nome { get; set; } = string.Empty;

    [Required(ErrorMessage = "O e-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "O e-mail informado não é válido.")]
    public string Email { get; set; } = string.Empty;

    [SenhaForte]
    public string? Senha { get; set; }

    [Required(ErrorMessage = "O perfil é obrigatório.")]
    public PerfilUsuario Perfil { get; set; }
}
