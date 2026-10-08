using OficinaMecanica.OS.Application.Validators;

namespace OficinaMecanica.OS.Application.UseCases.OrdemServico;

public record AbrirOrdemServicoRequest(
    [IdObrigatorio] Guid ClienteId,
    [IdObrigatorio] Guid VeiculoId,
    [IdObrigatorio] Guid FilialId,
    string? Observacoes
);
