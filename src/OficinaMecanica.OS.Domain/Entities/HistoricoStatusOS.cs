using System.Diagnostics.CodeAnalysis;
using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Domain.Entities;

public class HistoricoStatusOS
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrdemServicoId { get; private set; }
    public StatusOrdemServico StatusAnterior { get; private set; }
    public StatusOrdemServico StatusNovo { get; private set; }
    public DateTime DataAlteracao { get; private set; } = DateTime.UtcNow;

    [ExcludeFromCodeCoverage]
    protected HistoricoStatusOS() { }

    public HistoricoStatusOS(Guid ordemServicoId, StatusOrdemServico statusAnterior, StatusOrdemServico statusNovo)
    {
        OrdemServicoId = ordemServicoId;
        StatusAnterior = statusAnterior;
        StatusNovo = statusNovo;
    }
}
