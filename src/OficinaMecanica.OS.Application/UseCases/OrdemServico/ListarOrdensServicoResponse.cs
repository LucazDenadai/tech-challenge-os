using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Application.UseCases.OrdemServico;

public record ListarOrdensServicoItem(
    Guid Id,
    string Numero,
    StatusOrdemServico Status,
    Guid FilialId,
    DateTime DataAbertura
);
