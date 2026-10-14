using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OficinaMecanica.OS.Application.UseCases.Mensageria;
using OficinaMecanica.OS.Domain.Saga;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence;

// Registra a duração da Saga quando ela chega a um estado terminal (CARD-42a). Fica no SaveChanges porque toda
// transição passa por aqui, venha de mensagem, cancelamento, Admin ou prazo. Só conta depois que a gravação deu certo:
// um conflito de concorrência refaz a mensagem e não pode contar duas vezes.
public sealed class MetricasSagaInterceptor : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, List<(EstadoSaga Final, TimeSpan Duracao)>> _pendentes = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Separar(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Separar(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Registrar(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        Registrar(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is not null)
            _pendentes.Remove(eventData.Context);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        SaveChangesFailed(eventData);
        return Task.CompletedTask;
    }

    private void Separar(DbContext? contexto)
    {
        if (contexto is null)
            return;

        var agora = DateTime.UtcNow;
        var encerradas = contexto.ChangeTracker.Entries<SagaOrdemServico>()
            .Where(e => e.State == EntityState.Modified)
            .Select(e => (Entrada: e, Anterior: e.Property(s => s.Estado).OriginalValue))
            .Where(e => e.Entrada.Entity.Terminada && !SagaOrdemServico.EstadosTerminais.Contains(e.Anterior))
            .Select(e => (e.Entrada.Entity.Estado, agora - e.Entrada.Entity.CriadoEm))
            .ToList();
        if (encerradas.Count > 0)
            _pendentes.AddOrUpdate(contexto, encerradas);
    }

    private void Registrar(DbContext? contexto)
    {
        if (contexto is null || !_pendentes.TryGetValue(contexto, out var encerradas))
            return;

        _pendentes.Remove(contexto);
        foreach (var (final, duracao) in encerradas)
            MetricasSaga.Encerrada(final, duracao);
    }
}
