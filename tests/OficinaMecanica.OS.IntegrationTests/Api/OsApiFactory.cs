using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace OficinaMecanica.OS.IntegrationTests.Api;

// Sobe a API real (Program.cs, migrations e seed) contra um banco PostgreSQL vazio do Testcontainers.
public class OsApiFactory(string connectionString, IReadOnlyDictionary<string, string?>? extras = null) : WebApplicationFactory<Program>
{
    public const string SenhaSeed = "Senha@Demo123";
    public const string ApiKeyInterna = "chave-interna-de-teste";
    private const string JwtKey = "chave-jwt-de-teste-com-mais-de-32-caracteres";
    private const string JwtIssuer = "oficina-atendimento";
    private const string JwtAudience = "oficina-atendimento-api";

    public string ConnectionString => connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        builder.UseSetting("Jwt:Key", JwtKey);
        builder.UseSetting("Jwt:Issuer", JwtIssuer);
        builder.UseSetting("Jwt:Audience", JwtAudience);
        builder.UseSetting("Seed:SenhaUsuarios", SenhaSeed);
        builder.UseSetting("Interno:ApiKey", ApiKeyInterna);
        builder.UseSetting("RabbitMq:Enabled", "false");
        builder.UseSetting("Jaeger:Endpoint", "http://localhost:4318");

        foreach (var (chave, valor) in extras ?? new Dictionary<string, string?>())
            builder.UseSetting(chave, valor);
    }

    public async Task<HttpClient> ClienteFuncionarioAsync(string email)
    {
        var http = CreateClient();
        var resposta = await http.PostAsJsonAsync("/os/auth/login", new { email, senha = SenhaSeed });
        resposta.EnsureSuccessStatusCode();
        var corpo = await resposta.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", corpo!["token"]);
        return http;
    }

    // Token no formato emitido pela Lambda de CPF (RFC-003): role Cliente e claim cliente_id, mesma chave e issuer.
    public HttpClient ClienteFinal(Guid clienteId)
    {
        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: [new Claim("cliente_id", clienteId.ToString()), new Claim("role", "Cliente")],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)), SecurityAlgorithms.HmacSha256));

        var http = CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return http;
    }
}
