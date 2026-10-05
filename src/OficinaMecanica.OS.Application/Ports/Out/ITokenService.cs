namespace OficinaMecanica.OS.Application.Ports.Out;

public interface ITokenService
{
    string GerarToken(Guid usuarioId, string email, string perfil);
    bool VerificarSenha(string senhaPlana, string hashArmazenado);
    string HashSenha(string senhaPlana);
}
