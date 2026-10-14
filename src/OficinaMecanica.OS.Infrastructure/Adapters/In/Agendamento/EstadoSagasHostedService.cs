using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OficinaMecanica.OS.Application.UseCases.Mensageria;
using OficinaMecanica.OS.Application.UseCases.Saga;
using OficinaMecanica.OS.Domain.Saga;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence;

namespace OficinaMecanica.OS.Infrastructure.Adapters.In.Agendamento;

// Lê, a cada intervalo, quantas Sagas estão em cada estado não terminal e desde quando está a etapa mais antiga
// (CARD-42a). É o que mostra Saga parada e pagamento pendente: a métrica não depende de um evento chegar.
public class EstadoSagasHostedService(
    IServiceScopeFactory scopeFactory,
    ConfiguracaoSaga configuracao,
    ILogger<EstadoSagasHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(configuracao.IntervaloMetricasSegundos);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await AtualizarAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Falha ao ler o estado das Sagas para as métricas; nova tentativa no próximo intervalo.");
            }

            await Task.Delay(intervalo, stoppingToken);
        }
    }

    public async Task<IReadOnlyCollection<MetricasSaga.EstadoAtual>> AtualizarAsync(DateTime agora, CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();

        var leitura = await db.Sagas.AsNoTracking()
            .Where(s => !Enumerable.Contains(SagaOrdemServico.EstadosTerminais, s.Estado))
            .GroupBy(s => s.Estado)
            .Select(g => new { Estado = g.Key, Quantidade = g.Count(), EtapaMaisAntiga = g.Min(s => s.EtapaIniciadaEm) })
            .ToListAsync(ct);

        return MetricasSaga.AtualizarEstados(leitura.Select(l => (l.Estado, l.Quantidade, l.EtapaMaisAntiga)), agora);
    }
}
