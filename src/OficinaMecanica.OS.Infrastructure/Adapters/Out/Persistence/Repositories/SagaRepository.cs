using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using OficinaMecanica.OS.Application.Contratos.Saga;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Saga;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Outbox;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Repositories;

public class SagaRepository(AppDbContext context) : ISagaRepository
{
    public async Task AdicionarAsync(SagaOrdemServico saga, CancellationToken ct = default)
        => await context.Sagas.AddAsync(saga, ct);

    public Task<SagaOrdemServico?> ObterPorOsAsync(Guid osId, CancellationToken ct = default)
        => Completas.SingleOrDefaultAsync(s => s.OsId == osId, ct);

    public Task<SagaOrdemServico?> ObterAsync(Guid sagaId, CancellationToken ct = default)
        => Completas.SingleOrDefaultAsync(s => s.Id == sagaId, ct);

    public async Task<IReadOnlyList<Guid>> ListarComPrazoVencidoAsync(DateTime etapaIniciadaAte, int maximo, CancellationToken ct = default)
        => await context.Sagas
            .Where(s => SagaOrdemServico.EstadosComPrazo.Contains(s.Estado) && s.EtapaIniciadaEm <= etapaIniciadaAte)
            .OrderBy(s => s.EtapaIniciadaEm)
            .Select(s => s.Id)
            .Take(maximo)
            .ToListAsync(ct);

    private IQueryable<SagaOrdemServico> Completas => context.Sagas.Include(s => s.Transicoes).AsSplitQuery();
}

public class OutboxRepository(AppDbContext context) : IOutbox
{
    public void Adicionar(MensagemSaga mensagem)
    {
        var canal = CatalogoCanaisOS.Publicado(mensagem.GetType());
        context.Outbox.Add(new MensagemOutbox
        {
            MessageId = mensagem.MessageId,
            MessageType = mensagem.MessageType,
            Canal = canal.Endereco,
            CorrelationId = mensagem.CorrelationId,
            Payload = ComandoSaga.Serializar(mensagem),
            TraceParent = Activity.Current is { IdFormat: ActivityIdFormat.W3C } atividade ? atividade.Id : null,
            CriadaEmUtc = DateTimeOffset.UtcNow
        });
    }
}

public class Transacao(AppDbContext context) : ITransacao
{
    public async Task<T> ExecutarAsync<T>(Func<Task<T>> acao, CancellationToken ct = default)
    {
        await using var transacao = await context.Database.BeginTransactionAsync(ct);
        var resultado = await acao();
        await transacao.CommitAsync(ct);
        return resultado;
    }
}
