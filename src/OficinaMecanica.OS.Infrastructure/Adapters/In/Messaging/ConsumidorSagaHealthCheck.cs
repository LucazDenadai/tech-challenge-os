using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OficinaMecanica.OS.Infrastructure.Adapters.In.Messaging;

public class EstadoConsumidorSaga
{
    private volatile bool _consumindo;

    public bool Consumindo => _consumindo;

    public void Marcar(bool consumindo) => _consumindo = consumindo;
}

public class ConsumidorSagaHealthCheck(EstadoConsumidorSaga estado) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(estado.Consumindo
            ? HealthCheckResult.Healthy("Consumindo os canais da Saga.")
            : HealthCheckResult.Unhealthy("Sem conexão com o RabbitMQ."));
}
