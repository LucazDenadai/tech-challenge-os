using Microsoft.EntityFrameworkCore;
using OficinaMecanica.OS.Application.Ports.Out;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Repositories;

public abstract class BaseRepository<T> : IRepository<T> where T : class
{
    protected readonly AppDbContext _context;
    protected readonly DbSet<T> _dbSet;

    protected BaseRepository(AppDbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public virtual async Task<T?> ObterPorIdAsync(Guid id, CancellationToken ct = default)
        => await _dbSet.FindAsync([id], ct);

    public virtual async Task<IEnumerable<T>> ObterTodosAsync(CancellationToken ct = default)
        => await _dbSet.ToListAsync(ct);

    public async Task AdicionarAsync(T entidade, CancellationToken ct = default)
        => await _dbSet.AddAsync(entidade, ct);

    public virtual Task AtualizarAsync(T entidade, CancellationToken ct = default)
    {
        if (_context.Entry(entidade).State == EntityState.Detached)
            _dbSet.Update(entidade);
        return Task.CompletedTask;
    }

    public Task RemoverAsync(T entidade, CancellationToken ct = default)
    {
        _dbSet.Remove(entidade);
        return Task.CompletedTask;
    }

    public virtual async Task<int> SalvarAsync(CancellationToken ct = default)
    {
        try
        {
            return await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException(
                "O recurso foi alterado por outra operação simultânea. Tente novamente.");
        }
    }
}
