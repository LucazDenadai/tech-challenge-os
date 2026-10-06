using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;

namespace OficinaMecanica.OS.Application.UseCases.Auth;

public class AuthUseCase(IUsuarioRepository usuarioRepository, ITokenService tokenService)
{
    public async Task<string> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var usuario = await usuarioRepository.ObterPorEmailAsync(request.Email, ct)
            ?? throw new NotFoundException("Usuario", request.Email);

        if (!tokenService.VerificarSenha(request.Senha, usuario.SenhaHash))
            throw new InvalidOperationException("Senha incorreta.");

        return tokenService.GerarToken(usuario.Id, usuario.Email, usuario.Perfil.ToString());
    }
}
