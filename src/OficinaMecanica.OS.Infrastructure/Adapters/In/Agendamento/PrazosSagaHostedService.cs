using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OficinaMecanica.OS.Application.UseCases.Saga;

namespace OficinaMecanica.OS.Infrastructure.Adapters.In.Agendamento;

// Procura, a cada intervalo, Sagas paradas além do prazo em uma etapa que espera participante (CARD-40b).
// Cada Saga é tratada em escopo próprio: um conflito de concorrência (outra réplica ou uma mensagem chegando agora)
// não afeta as demais; a Saga é reavaliada no próximo intervalo. Não depende do broker: os comandos vão para a outbox.
public class PrazosSagaHostedService(
    IServiceScopeFactory scopeFactory,
    ConfiguracaoSaga configuracao,
    ILogger<PrazosSagaHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(configuracao.IntervaloVerificacaoSegundos);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await VerificarAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Falha ao verificar prazos da Saga; nova tentativa no próximo intervalo.");
            }

            await Task.Delay(intervalo, stoppingToken);
        }
    }

    public async Task VerificarAsync(DateTime agora, CancellationToken ct)
    {
        IReadOnlyList<Guid> vencidas;
        using (var escopo = scopeFactory.CreateScope())
            vencidas = await escopo.ServiceProvider.GetRequiredService<VerificarPrazoSagaUseCase>().ListarVencidasAsync(agora, ct);

        foreach (var sagaId in vencidas)
        {
            try
            {
                using var escopo = scopeFactory.CreateScope();
                await escopo.ServiceProvider.GetRequiredService<VerificarPrazoSagaUseCase>().ExecutarAsync(sagaId, agora, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Falha ao tratar o prazo da Saga {SagaId}; reavaliada no próximo intervalo.", sagaId);
            }
        }
    }
}
