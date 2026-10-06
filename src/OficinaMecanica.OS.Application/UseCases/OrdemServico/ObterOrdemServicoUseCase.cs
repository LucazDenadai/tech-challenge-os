using OficinaMecanica.OS.Application.Ports.Out;
using DomainOS = OficinaMecanica.OS.Domain.Entities.OrdemServico;

namespace OficinaMecanica.OS.Application.UseCases.OrdemServico;

public class ObterOrdemServicoUseCase(IOrdemServicoRepository repository)
{
    public async Task<OrdemServicoDetalheResponse?> ExecutarAsync(Guid id, CancellationToken ct = default)
    {
        var os = await repository.ObterComDetalhesAsync(id, ct);
        return os is null ? null : ToDetalhe(os);
    }

    internal static OrdemServicoDetalheResponse ToDetalhe(DomainOS os) => new(
        os.Id, os.Numero, os.Status, os.ClienteId, os.VeiculoId, os.FilialId,
        os.Observacoes, os.DataAbertura, os.DataFechamento,
        os.Historico.Select(h => new HistoricoStatusOSItem(h.StatusAnterior, h.StatusNovo, h.DataAlteracao)).ToList());
}
