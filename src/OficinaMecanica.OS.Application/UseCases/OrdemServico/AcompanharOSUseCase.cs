using OficinaMecanica.OS.Application.Ports.Out;

namespace OficinaMecanica.OS.Application.UseCases.OrdemServico;

public class AcompanharOSUseCase(IOrdemServicoRepository repository)
{
    public async Task<AcompanhamentoOSResponse?> ExecutarAsync(string numero, CancellationToken ct = default)
    {
        var os = await repository.ObterPorNumeroAsync(numero, ct);
        if (os is null) return null;

        return new AcompanhamentoOSResponse(
            os.Numero, os.Status, os.DataAbertura, os.DataFechamento,
            os.Historico.Select(h => new HistoricoStatusOSItem(h.StatusAnterior, h.StatusNovo, h.DataAlteracao)).ToList());
    }
}
