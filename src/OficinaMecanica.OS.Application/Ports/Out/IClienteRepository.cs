using OficinaMecanica.OS.Domain.Entities;

namespace OficinaMecanica.OS.Application.Ports.Out;

public interface IClienteRepository : IRepository<Cliente>
{
    Task<Cliente?> ObterPorDocumentoAsync(string documento, CancellationToken ct = default);
    Task<bool> DocumentoExisteAsync(string documento, Guid? excluirId = null, CancellationToken ct = default);
    Task<IEnumerable<Cliente>> BuscarAsync(string termo, CancellationToken ct = default);
}
