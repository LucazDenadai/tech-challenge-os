using OficinaMecanica.OS.Application.Ports.Out;

namespace OficinaMecanica.OS.Application.UseCases.OrdemServico;

public class AcompanharOSUseCase(IOrdemServicoRepository repository)
{
    // O cliente autenticado por CPF só acompanha as próprias ordens; OS de outro cliente é tratada como inexistente.
    public async Task<AcompanhamentoOSResponse?> ExecutarAsync(string numero, Guid clienteId, CancellationToken ct = default)
    {
        var os = await repository.ObterPorNumeroAsync(numero, ct);
        if (os is null || os.ClienteId != clienteId) return null;

        return new AcompanhamentoOSResponse(
            os.Numero, os.Status, os.DataAbertura, os.DataFechamento,
            os.Historico.Select(h => new HistoricoStatusOSItem(h.StatusAnterior, h.StatusNovo, h.DataAlteracao)).ToList());
    }
}
