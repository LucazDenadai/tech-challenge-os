using OficinaMecanica.OS.Domain.Entities;

namespace OficinaMecanica.OS.Application.Ports.Out;

public interface IVeiculoRepository : IRepository<Veiculo>
{
    Task<IEnumerable<Veiculo>> ObterPorClienteAsync(Guid clienteId, CancellationToken ct = default);
    Task<IEnumerable<Veiculo>> ObterPorDocumentoClienteAsync(string documento, CancellationToken ct = default);
    Task<Veiculo?> ObterPorPlacaAsync(string placa, CancellationToken ct = default);
    Task<bool> PossuiOrdensServicoAsync(Guid veiculoId, CancellationToken ct = default);
}
