using OficinaMecanica.OS.Domain.Entities;

namespace OficinaMecanica.OS.Application.Ports.Out;

public interface IFilialRepository : IRepository<Filial>
{
    Task<Filial?> ObterPorCodigoAsync(string codigo, CancellationToken ct = default);
}
