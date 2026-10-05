using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Application.UseCases.Usuario;

public record UsuarioResponse(Guid Id, string Nome, string Email, PerfilUsuario Perfil, bool Ativo);
