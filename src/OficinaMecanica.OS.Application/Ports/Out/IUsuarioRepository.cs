using OficinaMecanica.OS.Domain.Entities;

namespace OficinaMecanica.OS.Application.Ports.Out;

public interface IUsuarioRepository : IRepository<Usuario>
{
    Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken ct = default);
}
