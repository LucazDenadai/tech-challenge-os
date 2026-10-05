using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using OficinaMecanica.OS.Application.Ports.Out;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Security;

public class JwtTokenService : ITokenService
{
    private readonly IConfiguration _config;

    public JwtTokenService(IConfiguration config) => _config = config;

    public string GerarToken(Guid usuarioId, string email, string perfil)
    {
        var jwtKey = (_config["Jwt:Key"] is { Length: > 0 } k ? k : null)
            ?? Environment.GetEnvironmentVariable("JWT_KEY")
            ?? throw new InvalidOperationException("JWT_KEY não configurado.");

        var jwtIssuer = _config["Jwt:Issuer"] ?? Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "";
        var jwtAudience = _config["Jwt:Audience"] ?? Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "";
        var expiracaoMinutos = int.Parse(_config["Jwt:ExpiracaoMinutos"] ?? Environment.GetEnvironmentVariable("JWT_EXPIRACAO_MINUTOS") ?? "60");

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, usuarioId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(ClaimTypes.Role, perfil),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiracaoMinutos),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public bool VerificarSenha(string senhaPlana, string hashArmazenado)
        => BCrypt.Net.BCrypt.Verify(senhaPlana, hashArmazenado);

    public string HashSenha(string senhaPlana)
        => BCrypt.Net.BCrypt.HashPassword(senhaPlana);
}
