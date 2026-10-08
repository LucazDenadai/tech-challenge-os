using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace OficinaMecanica.OS.API.Auth;

// Credencial de API de escopo mínimo para chamadas internas, como a busca de cliente pela Lambda (ADR-015).
// Não substitui o JWT: vale só nos endpoints que pedem este esquema.
public class ApiKeyInternaHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IConfiguration configuration) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string Esquema = "ApiKeyInterna";
    public const string Cabecalho = "X-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Cabecalho, out var recebida) || string.IsNullOrEmpty(recebida))
            return Task.FromResult(AuthenticateResult.NoResult());

        var esperada = configuration["Interno:ApiKey"];
        if (string.IsNullOrEmpty(esperada))
            return Task.FromResult(AuthenticateResult.Fail("Interno:ApiKey não configurada."));

        // Comparação em tempo constante para não vazar o prefixo correto por tempo de resposta.
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(recebida.ToString()), Encoding.UTF8.GetBytes(esperada)))
            return Task.FromResult(AuthenticateResult.Fail("Chave de API inválida."));

        var identidade = new ClaimsIdentity([new Claim(ClaimTypes.Name, "servico-interno")], Esquema);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identidade), Esquema)));
    }
}
