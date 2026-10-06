using Microsoft.EntityFrameworkCore;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Entities;
using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Repositories;

public class OrdemServicoRepository : BaseRepository<OrdemServico>, IOrdemServicoRepository
{
    public OrdemServicoRepository(AppDbContext context) : base(context) { }

    public override Task AtualizarAsync(OrdemServico entidade, CancellationToken ct = default)
    {
        // AutoDetectChanges desabilitado para evitar que EF Core marque filhos não-rastreados
        // como Modified antes de serem adicionados explicitamente (causa DbUpdateConcurrencyException).
        _context.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            foreach (var historico in entidade.Historico)
            {
                var e = _context.Entry(historico);
                if (e.State == EntityState.Detached) e.State = EntityState.Added;
            }
        }
        finally
        {
            _context.ChangeTracker.AutoDetectChangesEnabled = true;
        }

        return base.AtualizarAsync(entidade, ct);
    }

    public override async Task<IEnumerable<OrdemServico>> ObterTodosAsync(CancellationToken ct = default)
        => await _dbSet
            .Include(o => o.Cliente)
            .Include(o => o.Veiculo)
            .Include(o => o.Historico)
            .OrderByDescending(o => o.DataAbertura)
            .ToListAsync(ct);

    public async Task<OrdemServico?> ObterComDetalhesAsync(Guid id, CancellationToken ct = default)
        => await _dbSet
            .Include(o => o.Cliente)
            .Include(o => o.Veiculo)
            .Include(o => o.Historico)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<OrdemServico?> ObterPorNumeroAsync(string numero, CancellationToken ct = default)
        => await _dbSet
            .Include(o => o.Veiculo)
            .Include(o => o.Historico)
            .FirstOrDefaultAsync(o => o.Numero == numero, ct);

    public async Task<IEnumerable<OrdemServico>> ObterPorClienteAsync(Guid clienteId, CancellationToken ct = default)
        => await _dbSet
            .Include(o => o.Cliente)
            .Include(o => o.Veiculo)
            .Where(o => o.ClienteId == clienteId)
            .OrderByDescending(o => o.DataAbertura)
            .ToListAsync(ct);

    public async Task<IEnumerable<OrdemServico>> ObterPorStatusAsync(StatusOrdemServico status, CancellationToken ct = default)
        => await _dbSet
            .Include(o => o.Cliente)
            .Include(o => o.Veiculo)
            .Where(o => o.Status == status)
            .OrderByDescending(o => o.DataAbertura)
            .ToListAsync(ct);

    public async Task<string> GerarNumeroAsync(CancellationToken ct = default)
    {
        var ano = DateTime.UtcNow.Year;
        var prefix = $"OS-{ano}-";

        var numeros = await _dbSet
            .Where(o => o.Numero.StartsWith(prefix))
            .Select(o => o.Numero)
            .ToListAsync(ct);

        var sequencia = numeros
            .Select(n => int.TryParse(n[prefix.Length..], out var seq) ? seq : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{(sequencia + 1):D4}";
    }
}
