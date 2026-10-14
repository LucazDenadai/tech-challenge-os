using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMecanica.OS.API.Extensions;
using OficinaMecanica.OS.Application.UseCases.Mensageria;
using OficinaMecanica.OS.Application.UseCases.Saga;
using OficinaMecanica.OS.Domain.Entities;
using OficinaMecanica.OS.Domain.Saga;
using OficinaMecanica.OS.Infrastructure.Adapters.In.Agendamento;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Repositories;
using OficinaMecanica.OS.IntegrationTests.Api;
using OficinaMecanica.OS.IntegrationTests.Fixtures;
using OpenTelemetry.Trace;

namespace OficinaMecanica.OS.IntegrationTests.Observabilidade;

// Telemetria do OS (CARD-42a): o que começa trace, a duração da Saga, o retrato dos estados e o /metrics.
[Collection(PostgresCollection.Nome)]
public class TelemetriaTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(ActivityKind.Server, SamplingDecision.RecordAndSample)]
    [InlineData(ActivityKind.Consumer, SamplingDecision.RecordAndSample)]
    [InlineData(ActivityKind.Producer, SamplingDecision.RecordAndSample)]
    [InlineData(ActivityKind.Client, SamplingDecision.Drop)]
    [InlineData(ActivityKind.Internal, SamplingDecision.Drop)]
    public void Amostrador_SoComecaTraceNoInicioDeUmFluxo(ActivityKind tipo, SamplingDecision esperado)
    {
        var parametros = new SamplingParameters(default, ActivityTraceId.CreateRandom(), "span", tipo);

        Assert.Equal(esperado, new AmostradorInicioDeFluxo().ShouldSample(parametros).Decision);
    }

    [Theory]
    [InlineData("/os/health", false)]
    [InlineData("/os/ready", false)]
    [InlineData("/metrics", false)]
    [InlineData("/os/api/ordens-servico", true)]
    [InlineData("/os/api/health-check-do-veiculo", true)]
    public void ProbesEScrape_NaoViramTrace(string caminho, bool rastreia)
        => Assert.Equal(rastreia, AmostradorInicioDeFluxo.DeveRastrear(new PathString(caminho)));

    [Fact]
    public async Task SagaQueTermina_RegistraDuracaoUmaVezComOEstadoFinal()
    {
        var (conn, osId) = await PrepararSagaAsync();
        var medidas = new List<(double Valor, string? Estado)>();
        using var ouvinte = Ouvir(MetricasSaga.NomeDuracao, (valor, rotulos) => medidas.Add((valor, Rotulo(rotulos, "estado_final"))));

        var opcoes = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(conn).AddInterceptors(new MetricasSagaInterceptor()).Options;
        await using (var db = new AppDbContext(opcoes))
        {
            var saga = (await new SagaRepository(db).ObterPorOsAsync(osId))!;
            Assert.True(saga.RejeitarDiagnostico(new Causa("DiagnosisRejected.v1", Guid.NewGuid()), "Veículo sem defeito."));
            await db.SaveChangesAsync();
            // Gravar de novo a Saga já terminada não conta outra vez.
            saga.MarcarAtualizado();
            await db.SaveChangesAsync();
        }

        var registro = Assert.Single(medidas, m => m.Estado == nameof(EstadoSaga.Cancelled));
        Assert.True(registro.Valor >= 0);
    }

    [Fact]
    public async Task RetratoDosEstados_ContaSagasAtivasEIdadeDaEtapaMaisAntiga()
    {
        var (conn, _) = await PrepararSagaAsync();
        await using var servicos = new ServiceCollection().AddDbContext<AppDbContext>(o => o.UseNpgsql(conn)).BuildServiceProvider();
        var leitor = new EstadoSagasHostedService(servicos.GetRequiredService<IServiceScopeFactory>(), new ConfiguracaoSaga(),
            NullLogger<EstadoSagasHostedService>.Instance);

        var estados = await leitor.AtualizarAsync(DateTime.UtcNow.AddMinutes(10), CancellationToken.None);

        var diagnostico = Assert.Single(estados, e => e.Estado == EstadoSaga.Diagnosing);
        Assert.Equal(1, diagnostico.Quantidade);
        Assert.InRange(diagnostico.IdadeEtapaMaisAntigaSegundos, 590, 700);
        // Estados vazios aparecem com zero; terminais não aparecem.
        Assert.Contains(estados, e => e is { Estado: EstadoSaga.ManualActionRequired, Quantidade: 0, IdadeEtapaMaisAntigaSegundos: 0 });
        Assert.DoesNotContain(estados, e => SagaOrdemServico.EstadosTerminais.Contains(e.Estado));
    }

    [Fact]
    public async Task Metrics_ExpoeIdentidadeDoServicoEMetricasDaSaga()
    {
        await using var api = new OsApiFactory(postgres.NovoBancoVazio(), new Dictionary<string, string?> { ["Servico:Versao"] = "sha-teste" });
        var cliente = api.CreateClient();

        // O retrato dos estados é lido em segundo plano logo na subida; espera a primeira leitura.
        string corpo = "";
        for (var i = 0; i < 20 && !corpo.Contains("os_sagas_por_estado"); i++)
        {
            await Task.Delay(500);
            corpo = await cliente.GetStringAsync("/metrics");
        }

        Assert.Contains("os_sagas_por_estado{", corpo);
        Assert.Contains("os_saga_idade_etapa_segundos{", corpo);
        Assert.Contains("service_name=\"os\"", corpo);
        Assert.Contains("service_version=\"sha-teste\"", corpo);
        Assert.Contains("deployment_environment=\"Test\"", corpo);
    }

    private async Task<(string Conn, Guid OsId)> PrepararSagaAsync()
    {
        var conn = postgres.NovoBancoVazio();
        await using var db = PostgresFixture.CriarContexto(conn);
        await db.Database.MigrateAsync();

        var filial = new Filial("FILIAL-SP", "Filial SP");
        var cliente = new Cliente("Cliente Teste", "11144477735", "cliente@cliente.example", "11900000000", "Rua A");
        var veiculo = new Veiculo(cliente.Id, "TST1A23", "Ford", "Ka", 2019, "Prata");
        var os = new OrdemServico("OS-0001", cliente.Id, veiculo.Id, filial.Id, "");
        db.AddRange(filial, cliente, veiculo, os);
        await new SagaRepository(db).AdicionarAsync(SagaOrdemServico.Iniciar(os));
        await db.SaveChangesAsync();
        return (conn, os.Id);
    }

    private static MeterListener Ouvir(string instrumento, Action<double, ReadOnlySpan<KeyValuePair<string, object?>>> aoMedir)
    {
        var ouvinte = new MeterListener
        {
            InstrumentPublished = (i, l) =>
            {
                if (i.Meter.Name == MetricasSaga.NomeMedidor && i.Name == instrumento)
                    l.EnableMeasurementEvents(i);
            }
        };
        ouvinte.SetMeasurementEventCallback<double>((_, valor, rotulos, _) => aoMedir(valor, rotulos));
        ouvinte.Start();
        return ouvinte;
    }

    private static string? Rotulo(ReadOnlySpan<KeyValuePair<string, object?>> rotulos, string nome)
    {
        foreach (var r in rotulos)
            if (r.Key == nome)
                return r.Value?.ToString();
        return null;
    }
}
