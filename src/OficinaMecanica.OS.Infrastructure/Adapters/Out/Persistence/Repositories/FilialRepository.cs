using Microsoft.EntityFrameworkCore;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Entities;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Repositories;

public class FilialRepository : BaseRepository<Filial>, IFilialRepository
{
    public FilialRepository(AppDbContext context) : base(context) { }

    public async Task<Filial?> ObterPorCodigoAsync(string codigo, CancellationToken ct = default)
        => await _dbSet.FirstOrDefaultAsync(f => f.Codigo == codigo, ct);
}
