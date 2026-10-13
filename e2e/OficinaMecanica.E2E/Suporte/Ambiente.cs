using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace OficinaMecanica.E2E.Suporte;

// Endereços e credenciais do ambiente de e2e/docker-compose.yml. As variáveis de ambiente permitem rodar
// dentro da rede do Compose (executar.sh) ou do host (portas publicadas).
public static class Ambiente
{
    public static string OsUrl => Variavel("E2E_OS_URL", "http://localhost:8090");
    public static string BillingUrl => Variavel("E2E_BILLING_URL", "http://localhost:8091");
    public static string OperacoesUrl => Variavel("E2E_OPERACOES_URL", "http://localhost:8092");

    // Mesmos valores do Compose. Ambiente descartável: não são segredos de nenhum ambiente real.
    private static string JwtKey => Variavel("E2E_JWT_KEY", "chave-do-ambiente-e2e-com-mais-de-32-caracteres");
    private static string SenhaUsuarios => Variavel("E2E_SENHA_USUARIOS", "Senha@E2e123");
    private const string Issuer = "oficina-atendimento";
    private const string Audience = "oficina-atendimento-api";

    public const string Atendente = "atendente@oficina.example";
    public const string Mecanico = "mecanico@oficina.example";
    public const string Admin = "admin@oficina.example";
    public const string CpfCliente = "11144477735";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, string> _tokens = new();

    // Funcionário entra pelo login do OS (usuários do seed); o token vale para os três serviços.
    public static async Task<HttpClient> FuncionarioAsync(string baseUrl, string email)
    {
        if (!_tokens.TryGetValue(email, out var token))
        {
            using var login = new HttpClient { BaseAddress = new Uri(OsUrl) };
            var resposta = await login.PostAsJsonAsync("/os/auth/login", new { email, senha = SenhaUsuarios });
            resposta.EnsureSuccessStatusCode();
            token = (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
            _tokens[email] = token;
        }
        return Cliente(baseUrl, token);
    }

    // Cliente final com token no formato da Lambda de CPF (RFC-003): role Cliente e claim cliente_id.
    public static HttpClient ClienteFinal(string baseUrl, Guid clienteId)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: [new Claim("cliente_id", clienteId.ToString()), new Claim("role", "Cliente")],
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)), SecurityAlgorithms.HmacSha256));
        return Cliente(baseUrl, new JwtSecurityTokenHandler().WriteToken(token));
    }

    // Os efeitos chegam por mensagem: as verificações esperam a condição em vez de dormir um tempo fixo.
    public static async Task<T> AguardarAsync<T>(Func<Task<T?>> obter, Func<T, bool> condicao, string descricao, int segundos = 60)
    {
        var limite = DateTime.UtcNow.AddSeconds(segundos);
        T? ultimo = default;
        while (DateTime.UtcNow < limite)
        {
            ultimo = await obter();
            if (ultimo is not null && condicao(ultimo))
                return ultimo;
            await Task.Delay(300);
        }
        throw new TimeoutException($"Não aconteceu em {segundos} s: {descricao}. Último valor: {Descrever(ultimo)}");
    }

    private static string Descrever(object? valor) => valor switch
    {
        null => "nenhum",
        JsonElement e => e.GetRawText(),
        _ => valor.ToString() ?? ""
    };

    private static HttpClient Cliente(string baseUrl, string token)
    {
        var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static string Variavel(string nome, string padrao)
        => Environment.GetEnvironmentVariable(nome) is { Length: > 0 } valor ? valor : padrao;
}
