using OficinaMecanica.OS.Application.Ports.Out;

namespace OficinaMecanica.OS.Application.UseCases.Auth;

public class AuthUseCase(IUsuarioRepository usuarioRepository, ITokenService tokenService)
{
    public async Task<string> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var usuario = await usuarioRepository.ObterPorEmailAsync(request.Email, ct);

        // Mesma resposta para e-mail inexistente, usuário desativado e senha errada: não revela quais e-mails existem.
        if (usuario is null || !usuario.Ativo || !tokenService.VerificarSenha(request.Senha, usuario.SenhaHash))
            throw new UnauthorizedAccessException("Credenciais inválidas.");

        return tokenService.GerarToken(usuario.Id, usuario.Email, usuario.Perfil.ToString());
    }
}
