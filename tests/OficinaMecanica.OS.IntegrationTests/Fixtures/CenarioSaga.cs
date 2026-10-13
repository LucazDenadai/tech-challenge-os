using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OficinaMecanica.OS.IntegrationTests.Api;

namespace OficinaMecanica.OS.IntegrationTests.Fixtures;

public sealed record SagaAberta(Guid OsId, Guid CorrelationId, Guid FilialId, Guid ClienteId);

// Abre uma OS pela API com os dados do seed e devolve as referências da Saga criada junto.
public static class CenarioSaga
{
    public const string Atendente = "atendente@oficina.example";
    public const string CpfSeed = "11144477735";

    public static async Task<SagaAberta> AbrirOsAsync(OsApiFactory factory)
    {
        var http = await factory.ClienteFuncionarioAsync(Atendente);
        var cliente = (await http.GetFromJsonAsync<JsonElement>($"/os/clientes?busca={CpfSeed}")).EnumerateArray().Single();
        var clienteId = cliente.GetProperty("id").GetGuid();
        var veiculo = (await http.GetFromJsonAsync<JsonElement>($"/os/veiculos/cliente/{clienteId}")).EnumerateArray().First();
        var filialId = (await http.GetFromJsonAsync<JsonElement>("/os/filiais")).EnumerateArray().Single().GetProperty("id").GetGuid();

        var resposta = await http.PostAsJsonAsync("/os/ordens-servico",
            new { clienteId, veiculoId = veiculo.GetProperty("id").GetGuid(), filialId });
        resposta.EnsureSuccessStatusCode();
        var osId = (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await using var db = PostgresFixture.CriarContexto(factory.ConnectionString);
        var sagaId = await db.Sagas.Where(s => s.OsId == osId).Select(s => s.Id).SingleAsync();
        return new SagaAberta(osId, sagaId, filialId, clienteId);
    }
}
