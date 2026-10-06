using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Application.UseCases.OrdemServico;

public record ConsultarStatusOSResponse(
    Guid Id,
    string Numero,
    StatusOrdemServico Status,
    IReadOnlyCollection<HistoricoStatusOSItem> Historico
);

public record HistoricoStatusOSItem(
    StatusOrdemServico StatusAnterior,
    StatusOrdemServico StatusNovo,
    DateTime AlteradoEm
);
