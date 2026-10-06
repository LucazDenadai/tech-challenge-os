namespace OficinaMecanica.OS.Application.UseCases.OrdemServico;

public record AbrirOrdemServicoRequest(
    Guid ClienteId,
    Guid VeiculoId,
    Guid FilialId,
    string Observacoes
);
