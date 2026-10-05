using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Application.UseCases.OrdemServico;

public class ListarOrdensServicoUseCase(IOrdemServicoRepository repository)
{
    private static readonly Dictionary<StatusOrdemServico, int> _prioridade = new()
    {
        [StatusOrdemServico.EmExecucao] = 1,
        [StatusOrdemServico.AguardandoPagamento] = 2,
        [StatusOrdemServico.AguardandoAprovacao] = 3,
        [StatusOrdemServico.EmDiagnostico] = 4,
    };

    public async Task<IReadOnlyCollection<ListarOrdensServicoItem>> ExecutarAsync(CancellationToken ct = default)
    {
        var todas = await repository.ObterTodosAsync(ct);

        return todas
            .Where(os => _prioridade.ContainsKey(os.Status))
            .OrderBy(os => _prioridade[os.Status])
            .ThenBy(os => os.DataAbertura)
            .Select(os => new ListarOrdensServicoItem(os.Id, os.Numero, os.Status, os.FilialId, os.DataAbertura))
            .ToList()
            .AsReadOnly();
    }
}
