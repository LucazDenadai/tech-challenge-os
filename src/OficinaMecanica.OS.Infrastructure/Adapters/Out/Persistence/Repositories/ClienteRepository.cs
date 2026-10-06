using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Entities;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Repositories;

public class ClienteRepository : BaseRepository<Cliente>, IClienteRepository
{
    public ClienteRepository(AppDbContext context) : base(context) { }

    [ExcludeFromCodeCoverage]
    public async Task<Cliente?> ObterPorDocumentoAsync(string documento, CancellationToken ct = default)
        => await _dbSet.FirstOrDefaultAsync(c => c.Documento == documento, ct);

    public async Task<bool> DocumentoExisteAsync(string documento, Guid? excluirId = null, CancellationToken ct = default)
        => await _dbSet.AnyAsync(c => c.Documento == documento && c.Ativo && (excluirId == null || c.Id != excluirId), ct);

    public async Task<IEnumerable<Cliente>> BuscarAsync(string termo, CancellationToken ct = default)
    {
        var pattern = $"%{termo}%";
        return await _dbSet.Where(c =>
            c.Ativo && (
            EF.Functions.ILike(c.Nome, pattern) ||
            c.Documento.Contains(termo) ||
            EF.Functions.ILike(c.Email, pattern)))
        .ToListAsync(ct);
    }
}
