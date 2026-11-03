using OficinaMecanica.OS.Domain.Entities;

namespace OficinaMecanica.OS.Application.Ports.Out;

public interface IOrdemServicoRepository : IRepository<OrdemServico>
{
    Task<OrdemServico?> ObterComDetalhesAsync(Guid id, CancellationToken ct = default);
    Task<OrdemServico?> ObterPorNumeroAsync(string numero, CancellationToken ct = default);
    Task<string> GerarNumeroAsync(CancellationToken ct = default);
}
