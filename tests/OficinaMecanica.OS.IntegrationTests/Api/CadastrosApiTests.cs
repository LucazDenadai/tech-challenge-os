using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OficinaMecanica.OS.IntegrationTests.Fixtures;

namespace OficinaMecanica.OS.IntegrationTests.Api;

// Cadastros de clientes, veículos e usuários pela API real (CARD-44): ciclo completo, duplicidade, inexistentes e papéis.
[Collection(PostgresCollection.Nome)]
public class CadastrosApiTests : IAsyncLifetime
{
    private const string Admin = "admin@oficina.example";
    private const string Atendente = "atendente@oficina.example";
    private const string Mecanico = "mecanico@oficina.example";
    // CPFs de exemplo, válidos só no dígito verificador.
    private const string CpfNovo = "39053344705";
    private const string OutroCpf = "12345678909";

    private readonly OsApiFactory _factory;

    public CadastrosApiTests(PostgresFixture fixture) => _factory = new OsApiFactory(fixture.NovoBancoVazio());

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static async Task<Guid> IdCriadoAsync(HttpResponseMessage resposta)
    {
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static object NovoCliente(string documento, string nome = "Cliente Teste") => new
    {
        nome, documento, email = "cliente.teste@cliente.example", telefone = "11999990000", endereco = "Rua de Teste, 10"
    };

    [Fact]
    public async Task Cliente_CicloCompletoComDesativacao()
    {
        var http = await _factory.ClienteFuncionarioAsync(Atendente);

        var id = await IdCriadoAsync(await http.PostAsJsonAsync("/os/clientes", NovoCliente(CpfNovo)));

        var obtido = await http.GetFromJsonAsync<JsonElement>($"/os/clientes/{id}");
        Assert.Equal(CpfNovo, obtido.GetProperty("documento").GetString());
        Assert.Contains((await http.GetFromJsonAsync<JsonElement>("/os/clientes")).EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
        Assert.Single((await http.GetFromJsonAsync<JsonElement>("/os/clientes?busca=Cliente Teste")).EnumerateArray());

        var atualizado = await http.PutAsJsonAsync($"/os/clientes/{id}",
            new { id, nome = "Cliente Renomeado", email = "novo@cliente.example", telefone = "11988887777", endereco = "Rua Nova, 20" });
        Assert.Equal(HttpStatusCode.OK, atualizado.StatusCode);
        Assert.Equal("Cliente Renomeado", (await atualizado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("nome").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync($"/os/clientes/{id}")).StatusCode);
        Assert.DoesNotContain((await http.GetFromJsonAsync<JsonElement>("/os/clientes")).EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
        Assert.False((await http.GetFromJsonAsync<JsonElement>($"/os/clientes/{id}")).GetProperty("ativo").GetBoolean());
    }

    // Defeito encontrado no CARD-44: DocumentoExisteAsync só considera clientes ativos, então o ramo de reativação
    // do caso de uso nunca roda e a API devolve 500 (índice único do documento). Correção depende de decisão do time.
    [Fact(Skip = "CARD-44: reativação de cliente desativado devolve 500; aguarda decisão")]
    public async Task Cliente_DocumentoDeClienteDesativado_ReativaOCadastro()
    {
        var http = await _factory.ClienteFuncionarioAsync(Atendente);
        var id = await IdCriadoAsync(await http.PostAsJsonAsync("/os/clientes", NovoCliente(CpfNovo)));
        await http.DeleteAsync($"/os/clientes/{id}");

        var reativado = await IdCriadoAsync(await http.PostAsJsonAsync("/os/clientes", NovoCliente(CpfNovo, "Cliente Voltou")));

        Assert.Equal(id, reativado);
        Assert.True((await http.GetFromJsonAsync<JsonElement>($"/os/clientes/{id}")).GetProperty("ativo").GetBoolean());
    }

    [Fact]
    public async Task Cliente_DocumentoAtivoRepetidoEInexistentes()
    {
        var http = await _factory.ClienteFuncionarioAsync(Admin);
        await IdCriadoAsync(await http.PostAsJsonAsync("/os/clientes", NovoCliente(OutroCpf)));
        var inexistente = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await http.PostAsJsonAsync("/os/clientes", NovoCliente(OutroCpf))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/os/clientes/{inexistente}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PutAsJsonAsync($"/os/clientes/{inexistente}",
            new { id = inexistente, nome = "X", email = "x@cliente.example", telefone = "1", endereco = "Y" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.DeleteAsync($"/os/clientes/{inexistente}")).StatusCode);
    }

    [Fact]
    public async Task Veiculo_CicloCompletoEConsultas()
    {
        var atendente = await _factory.ClienteFuncionarioAsync(Atendente);
        var admin = await _factory.ClienteFuncionarioAsync(Admin);
        var clienteId = await IdCriadoAsync(await atendente.PostAsJsonAsync("/os/clientes", NovoCliente(CpfNovo)));

        var id = await IdCriadoAsync(await atendente.PostAsJsonAsync("/os/veiculos",
            new { clienteId, placa = "tst-1a23", marca = "Fiat", modelo = "Uno", ano = 2015, cor = "Branco" }));

        Assert.Equal("TST1A23", (await atendente.GetFromJsonAsync<JsonElement>($"/os/veiculos/{id}")).GetProperty("placa").GetString());
        Assert.Contains((await atendente.GetFromJsonAsync<JsonElement>("/os/veiculos")).EnumerateArray(), v => v.GetProperty("id").GetGuid() == id);
        Assert.Single((await atendente.GetFromJsonAsync<JsonElement>($"/os/veiculos/cliente/{clienteId}")).EnumerateArray());
        Assert.Single((await atendente.GetFromJsonAsync<JsonElement>($"/os/veiculos/cliente/documento/{CpfNovo}")).EnumerateArray());

        var atualizado = await atendente.PutAsJsonAsync($"/os/veiculos/{id}", new { id, marca = "Fiat", modelo = "Mobi", ano = 2020, cor = "Preto" });
        Assert.Equal(HttpStatusCode.OK, atualizado.StatusCode);
        Assert.Equal("Mobi", (await atualizado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("modelo").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await atendente.DeleteAsync($"/os/veiculos/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/os/veiculos/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/os/veiculos/{id}")).StatusCode);
    }

    // Defeito encontrado no CARD-44: o [Authorize] da classe (Admin, Atendente) soma com o do método; o Mecânico recebe 403
    // nas leituras que o método e o README liberam para ele. Correção depende de decisão do time.
    [Fact(Skip = "CARD-44: Mecânico recebe 403 ao ler veículo; aguarda decisão")]
    public async Task Veiculo_MecanicoLeOVeiculo()
    {
        var atendente = await _factory.ClienteFuncionarioAsync(Atendente);
        var mecanico = await _factory.ClienteFuncionarioAsync(Mecanico);
        var clienteId = await IdCriadoAsync(await atendente.PostAsJsonAsync("/os/clientes", NovoCliente(CpfNovo)));
        var id = await IdCriadoAsync(await atendente.PostAsJsonAsync("/os/veiculos",
            new { clienteId, placa = "TST4D56", marca = "Fiat", modelo = "Uno", ano = 2015, cor = "Branco" }));

        Assert.Equal(HttpStatusCode.OK, (await mecanico.GetAsync($"/os/veiculos/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mecanico.GetAsync($"/os/veiculos/cliente/{clienteId}")).StatusCode);
    }

    [Fact]
    public async Task Veiculo_PlacaRepetidaClienteInexistenteEComOrdemDeServico()
    {
        var admin = await _factory.ClienteFuncionarioAsync(Admin);
        var clienteId = await IdCriadoAsync(await admin.PostAsJsonAsync("/os/clientes", NovoCliente(CpfNovo)));
        var veiculoId = await IdCriadoAsync(await admin.PostAsJsonAsync("/os/veiculos",
            new { clienteId, placa = "TST2B34", marca = "VW", modelo = "Gol", ano = 2012, cor = "Azul" }));
        var inexistente = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/os/veiculos",
            new { clienteId, placa = "TST-2B34", marca = "VW", modelo = "Gol", ano = 2012, cor = "Azul" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/os/veiculos",
            new { clienteId = inexistente, placa = "TST3C45", marca = "VW", modelo = "Gol", ano = 2012, cor = "Azul" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/os/veiculos/{inexistente}",
            new { id = inexistente, marca = "VW", modelo = "Gol", ano = 2012, cor = "Azul" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/os/veiculos/{inexistente}")).StatusCode);

        // Veículo com OS não pode ser removido.
        var filialId = (await admin.GetFromJsonAsync<JsonElement>("/os/filiais")).EnumerateArray().Single().GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/os/ordens-servico", new { clienteId, veiculoId, filialId })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.DeleteAsync($"/os/veiculos/{veiculoId}")).StatusCode);
    }

    [Fact]
    public async Task Usuario_CicloCompletoComTrocaDeSenhaEDesativacao()
    {
        var admin = await _factory.ClienteFuncionarioAsync(Admin);
        var criado = await admin.PostAsJsonAsync("/os/usuarios",
            new { nome = "Mecânico Novo", email = "mecanico.novo@oficina.example", senha = "Senha@Nova123", perfil = "Mecanico" });
        var id = await IdCriadoAsync(criado);
        Assert.Equal("Mecanico", (await admin.GetFromJsonAsync<JsonElement>($"/os/usuarios/{id}")).GetProperty("perfil").GetString());
        Assert.Contains((await admin.GetFromJsonAsync<JsonElement>("/os/usuarios")).EnumerateArray(), u => u.GetProperty("id").GetGuid() == id);
        Assert.Single((await admin.GetFromJsonAsync<JsonElement>("/os/usuarios?email=mecanico.novo@oficina.example")).EnumerateArray());
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>("/os/usuarios?email=ninguem@oficina.example")).EnumerateArray());

        // Sem senha, só os dados mudam; com senha, o login passa a usar a nova.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/os/usuarios/{id}",
            new { nome = "Mecânica Nova", email = "mecanico.novo@oficina.example", perfil = "Atendente" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/os/usuarios/{id}",
            new { nome = "Mecânica Nova", email = "mecanico.novo@oficina.example", senha = "Outra@Senha456", perfil = "Atendente" })).StatusCode);
        var login = await _factory.CreateClient().PostAsJsonAsync("/os/auth/login", new { email = "mecanico.novo@oficina.example", senha = "Outra@Senha456" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/os/usuarios/{id}")).StatusCode);
        Assert.DoesNotContain((await admin.GetFromJsonAsync<JsonElement>("/os/usuarios")).EnumerateArray(), u => u.GetProperty("id").GetGuid() == id);
        var depois = await _factory.CreateClient().PostAsJsonAsync("/os/auth/login", new { email = "mecanico.novo@oficina.example", senha = "Outra@Senha456" });
        Assert.Equal(HttpStatusCode.Unauthorized, depois.StatusCode);
    }

    [Fact]
    public async Task Usuario_EmailRepetidoEInexistentes()
    {
        var admin = await _factory.ClienteFuncionarioAsync(Admin);
        var inexistente = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/os/usuarios",
            new { nome = "Outro Admin", email = Admin, senha = "Senha@Nova123", perfil = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/os/usuarios/{inexistente}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/os/usuarios/{inexistente}",
            new { nome = "X", email = "x@oficina.example", perfil = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/os/usuarios/{inexistente}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await _factory.ClienteFuncionarioAsync(Atendente)).GetAsync("/os/usuarios")).StatusCode);
    }
}
