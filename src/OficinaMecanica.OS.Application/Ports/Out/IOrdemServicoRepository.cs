using OficinaMecanica.OS.Domain.Entities;
using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Application.Ports.Out;

public interface IOrdemServicoRepository : IRepository<OrdemServico>
{
    Task<OrdemServico?> ObterComDetalhesAsync(Guid id, CancellationToken ct = default);
    Task<OrdemServico?> ObterPorNumeroAsync(string numero, CancellationToken ct = default);
    Task<IEnumerable<OrdemServico>> ObterPorClienteAsync(Guid clienteId, CancellationToken ct = default);
    Task<IEnumerable<OrdemServico>> ObterPorStatusAsync(StatusOrdemServico status, CancellationToken ct = default);
    Task<string> GerarNumeroAsync(CancellationToken ct = default);
}
