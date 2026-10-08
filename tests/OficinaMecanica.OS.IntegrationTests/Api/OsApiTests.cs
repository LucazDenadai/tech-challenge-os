using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OficinaMecanica.OS.IntegrationTests.Fixtures;

namespace OficinaMecanica.OS.IntegrationTests.Api;

// Testes de API do CARD-37b: happy path, validação, autorização e falhas, contra a API e o banco reais.
[Collection(PostgresCollection.Nome)]
public class OsApiTests : IAsyncLifetime
{
    private const string Admin = "admin@oficina.example";
    private const string Atendente = "atendente@oficina.example";
    private const string Mecanico = "mecanico@oficina.example";
    private const string CpfSeed = "11144477735";

    private readonly OsApiFactory _factory;

    public OsApiTests(PostgresFixture fixture) => _factory = new OsApiFactory(fixture.NovoBancoVazio());

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    // ── Autenticação ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_UsuarioDoSeed_Retorna200ComToken()
    {
        var resposta = await _factory.CreateClient().PostAsJsonAsync("/os/auth/login", new { email = Admin, senha = OsApiFactory.SenhaSeed });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(corpo.GetProperty("token").GetString()));
    }

    [Theory]
    [InlineData(Admin, "SenhaErrada@1")]
    [InlineData("ninguem@oficina.example", OsApiFactory.SenhaSeed)]
    public async Task Login_CredenciaisInvalidas_Retorna401SemDistinguirOMotivo(string email, string senha)
    {
        var resposta = await _factory.CreateClient().PostAsJsonAsync("/os/auth/login", new { email, senha });

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Credenciais inválidas.", corpo.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task EndpointProtegido_SemToken_Retorna401()
    {
        var resposta = await _factory.CreateClient().GetAsync("/os/ordens-servico");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Mecanico_CriarCliente_Retorna403()
    {
        var http = await _factory.ClienteFuncionarioAsync(Mecanico);

        var resposta = await http.PostAsJsonAsync("/os/clientes", new
        {
            nome = "Novo Cliente", documento = "52998224725", email = "novo@cliente.example", telefone = "11900000000", endereco = "Rua Demo"
        });

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task TokenDeCliente_NaoAcessaCadastros()
    {
        var resposta = await _factory.ClienteFinal(Guid.NewGuid()).GetAsync("/os/clientes");

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    // ── Ordem de serviço ──────────────────────────────────────────────────────

    [Fact]
    public async Task AbrirOS_ConsultarStatusEHistorico_CancelarERecusarEntrega()
    {
        var http = await _factory.ClienteFuncionarioAsync(Atendente);
        var (clienteId, veiculoId, filialId) = await DadosDoSeedAsync(http);

        // Abrir
        var abertura = await http.PostAsJsonAsync("/os/ordens-servico", new { clienteId, veiculoId, filialId, observacoes = "Barulho no freio" });
        Assert.Equal(HttpStatusCode.Created, abertura.StatusCode);
        var os = await abertura.Content.ReadFromJsonAsync<JsonElement>();
        var osId = os.GetProperty("id").GetGuid();
        Assert.StartsWith("OS-", os.GetProperty("numero").GetString());
        Assert.NotNull(abertura.Headers.Location);

        // Detalhe preserva a filial
        var detalhe = await http.GetFromJsonAsync<JsonElement>($"/os/ordens-servico/{osId}");
        Assert.Equal(filialId, detalhe.GetProperty("filialId").GetGuid());

        // Status inicial e histórico vazio
        var status = await http.GetFromJsonAsync<JsonElement>($"/os/ordens-servico/{osId}/status");
        Assert.Equal("EmDiagnostico", status.GetProperty("status").GetString());
        Assert.Empty((await http.GetFromJsonAsync<JsonElement>($"/os/ordens-servico/{osId}/historico")).EnumerateArray());

        // Cancelamento registra a transição no histórico
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsync($"/os/ordens-servico/{osId}/cancelamento", null)).StatusCode);
        var historico = (await http.GetFromJsonAsync<JsonElement>($"/os/ordens-servico/{osId}/historico")).EnumerateArray().ToList();
        var transicao = Assert.Single(historico);
        Assert.Equal("EmDiagnostico", transicao.GetProperty("statusAnterior").GetString());
        Assert.Equal("Cancelada", transicao.GetProperty("statusNovo").GetString());

        // Entrega só a partir de Finalizada
        var entrega = await http.PostAsync($"/os/ordens-servico/{osId}/entrega", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, entrega.StatusCode);
    }

    [Fact]
    public async Task AbrirOS_SemCamposObrigatorios_Retorna400ENaoPersiste()
    {
        var http = await _factory.ClienteFuncionarioAsync(Atendente);

        var resposta = await http.PostAsJsonAsync("/os/ordens-servico", new { observacoes = "sem cliente, veículo nem filial" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        var erros = corpo.GetProperty("erros");
        Assert.True(erros.TryGetProperty("ClienteId", out _));
        Assert.True(erros.TryGetProperty("VeiculoId", out _));
        Assert.True(erros.TryGetProperty("FilialId", out _));
        Assert.Empty((await http.GetFromJsonAsync<JsonElement>("/os/ordens-servico")).EnumerateArray());
    }

    [Fact]
    public async Task AbrirOS_VeiculoDeOutroCliente_Retorna422()
    {
        var http = await _factory.ClienteFuncionarioAsync(Atendente);
        var (_, veiculoId, filialId) = await DadosDoSeedAsync(http);
        var outroCliente = (await http.GetFromJsonAsync<JsonElement>("/os/clientes?busca=52998224725")).EnumerateArray().Single().GetProperty("id").GetGuid();

        var resposta = await http.PostAsJsonAsync("/os/ordens-servico", new { clienteId = outroCliente, veiculoId, filialId });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
    }

    [Fact]
    public async Task ConsultarStatus_OSInexistente_Retorna404()
    {
        var http = await _factory.ClienteFuncionarioAsync(Mecanico);

        var resposta = await http.GetAsync($"/os/ordens-servico/{Guid.NewGuid()}/status");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    // ── Acompanhamento pelo cliente ───────────────────────────────────────────

    [Fact]
    public async Task Acompanhar_SoOClienteDonoVeAOS_SemIdsInternos()
    {
        var http = await _factory.ClienteFuncionarioAsync(Atendente);
        var (clienteId, veiculoId, filialId) = await DadosDoSeedAsync(http);
        var os = await (await http.PostAsJsonAsync("/os/ordens-servico", new { clienteId, veiculoId, filialId })).Content.ReadFromJsonAsync<JsonElement>();
        var numero = os.GetProperty("numero").GetString();

        var dono = await _factory.ClienteFinal(clienteId).GetAsync($"/os/ordens-servico/acompanhar/{numero}");
        var outro = await _factory.ClienteFinal(Guid.NewGuid()).GetAsync($"/os/ordens-servico/acompanhar/{numero}");
        var funcionario = await http.GetAsync($"/os/ordens-servico/acompanhar/{numero}");

        Assert.Equal(HttpStatusCode.OK, dono.StatusCode);
        var corpo = await dono.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("EmDiagnostico", corpo.GetProperty("status").GetString());
        Assert.False(corpo.TryGetProperty("id", out _));
        Assert.False(corpo.TryGetProperty("clienteId", out _));
        Assert.Equal(HttpStatusCode.NotFound, outro.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, funcionario.StatusCode);
    }

    // ── Endpoint interno da Lambda ────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("chave-errada")]
    public async Task Interno_SemChaveValida_Retorna401(string? chave)
    {
        var http = _factory.CreateClient();
        if (chave is not null) http.DefaultRequestHeaders.Add("X-Api-Key", chave);

        var resposta = await http.PostAsJsonAsync("/os/interno/clientes/busca-por-cpf", new { cpf = CpfSeed });

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Interno_JwtDeFuncionarioNaoSubstituiAChave()
    {
        var http = await _factory.ClienteFuncionarioAsync(Admin);

        var resposta = await http.PostAsJsonAsync("/os/interno/clientes/busca-por-cpf", new { cpf = CpfSeed });

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Interno_CpfCadastrado_DevolveSoIdEAtivo()
    {
        var resposta = await ClienteInterno().PostAsJsonAsync("/os/interno/clientes/busca-por-cpf", new { cpf = "111.444.777-35" });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["clienteId", "ativo"], corpo.EnumerateObject().Select(p => p.Name));
        Assert.True(corpo.GetProperty("ativo").GetBoolean());
    }

    [Theory]
    [InlineData("39053344705", HttpStatusCode.NotFound)]
    [InlineData("12345678900", HttpStatusCode.BadRequest)]
    public async Task Interno_CpfSemCadastroOuInvalido(string cpf, HttpStatusCode esperado)
    {
        var resposta = await ClienteInterno().PostAsJsonAsync("/os/interno/clientes/busca-por-cpf", new { cpf });

        Assert.Equal(esperado, resposta.StatusCode);
    }

    // ── Operação ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/os/health")]
    [InlineData("/os/ready")]
    public async Task HealthEReadiness_Retornam200(string rota)
    {
        var resposta = await _factory.CreateClient().GetAsync(rota);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Swagger_PublicaOsEndpointsDoServico()
    {
        var swagger = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/os/swagger/v1/swagger.json");

        var rotas = swagger.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("/os/auth/login", rotas);
        Assert.Contains("/os/ordens-servico", rotas);
        Assert.Contains("/os/ordens-servico/{id}/historico", rotas);
        Assert.Contains("/os/interno/clientes/busca-por-cpf", rotas);
        Assert.DoesNotContain(rotas, r => r.EndsWith("/status") && swagger.GetProperty("paths").GetProperty(r).TryGetProperty("put", out _));

        // Evidência do CARD-37b: com EXPORTAR_OPENAPI definido, grava o OpenAPI gerado nesse caminho (docs/openapi/os-v1.json).
        if (Environment.GetEnvironmentVariable("EXPORTAR_OPENAPI") is { Length: > 0 } caminho)
            await File.WriteAllTextAsync(caminho, JsonSerializer.Serialize(swagger, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    [Fact]
    public async Task Resposta_DevolveCorrelationIdRecebido()
    {
        var correlationId = Guid.NewGuid().ToString();
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        var resposta = await http.GetAsync("/os/health");

        Assert.Equal(correlationId, resposta.Headers.GetValues("X-Correlation-Id").Single());
    }

    private HttpClient ClienteInterno()
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", OsApiFactory.ApiKeyInterna);
        return http;
    }

    private static async Task<(Guid ClienteId, Guid VeiculoId, Guid FilialId)> DadosDoSeedAsync(HttpClient http)
    {
        var cliente = (await http.GetFromJsonAsync<JsonElement>($"/os/clientes?busca={CpfSeed}")).EnumerateArray().Single();
        var clienteId = cliente.GetProperty("id").GetGuid();
        var veiculo = (await http.GetFromJsonAsync<JsonElement>($"/os/veiculos/cliente/{clienteId}")).EnumerateArray().First();
        var filial = (await http.GetFromJsonAsync<JsonElement>("/os/filiais")).EnumerateArray().Single();
        return (clienteId, veiculo.GetProperty("id").GetGuid(), filial.GetProperty("id").GetGuid());
    }
}
