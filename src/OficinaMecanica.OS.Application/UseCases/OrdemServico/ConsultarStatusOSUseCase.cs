using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;

namespace OficinaMecanica.OS.Application.UseCases.OrdemServico;

public class ConsultarStatusOSUseCase(IOrdemServicoRepository repository)
{
    public async Task<ConsultarStatusOSResponse> ExecutarAsync(Guid id, CancellationToken ct = default)
    {
        var os = await repository.ObterComDetalhesAsync(id, ct)
            ?? throw new NotFoundException("OrdemServico", id);

        var historico = os.Historico
            .Select(h => new HistoricoStatusOSItem(h.StatusAnterior, h.StatusNovo, h.DataAlteracao))
            .ToList()
            .AsReadOnly();

        return new ConsultarStatusOSResponse(os.Id, os.Numero, os.Status, historico);
    }
}
