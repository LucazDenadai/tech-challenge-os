using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OficinaMecanica.OS.Infrastructure;
using OficinaMecanica.OS.Infrastructure.Adapters.In.Agendamento;
using OficinaMecanica.OS.IntegrationTests.Api;
using OficinaMecanica.OS.IntegrationTests.Fixtures;

namespace OficinaMecanica.OS.IntegrationTests.Infraestrutura;

// Prazos e retrato das Sagas com o banco fora do ar: viram aviso e tentam de novo no próximo intervalo.
[Collection(PostgresCollection.Nome)]
public class AgendamentosTests(PostgresFixture postgres)
{
    [Fact]
    public async Task BancoForaDoAr_PrazosERetratoAvisamEContinuam()
    {
        var logs = new LogsCapturados();
        await using var servicos = new ServiceCollection()
            .AddLogging(l => l.AddProvider(logs))
            .AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=1;Database=inexistente;Username=x;Password=x;Timeout=1",
                ["Saga:IntervaloVerificacaoSegundos"] = "1",
                ["Saga:IntervaloMetricasSegundos"] = "1"
            }).Build())
            .BuildServiceProvider();
        var agendamentos = servicos.GetServices<IHostedService>().Where(s => s is PrazosSagaHostedService or EstadoSagasHostedService).ToList();

        foreach (var agendamento in agendamentos)
            await agendamento.StartAsync(CancellationToken.None);
        await Aguardar(() => logs.Contar("Falha ao verificar prazos da Saga") >= 2 && logs.Contar("Falha ao ler o estado das Sagas") >= 2);
        foreach (var agendamento in agendamentos)
            await agendamento.StopAsync(CancellationToken.None);

        Assert.Equal(2, agendamentos.Count);
    }

    [Fact]
    public async Task RotaInterna_SemChaveConfigurada_RecusaAChamada()
    {
        await using var factory = new OsApiFactory(postgres.NovoBancoVazio(), new Dictionary<string, string?> { ["Interno:ApiKey"] = "" });
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", "qualquer-chave");

        var resposta = await http.PostAsJsonAsync("/os/interno/clientes/busca-por-cpf", new { cpf = "11144477735" });

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    private static async Task Aguardar(Func<bool> condicao)
    {
        var fim = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < fim)
        {
            if (condicao()) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("Condição não atingida em 30 s.");
    }
}
