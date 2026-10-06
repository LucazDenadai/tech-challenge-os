using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Application.UseCases.OrdemServico;

public record OrdemServicoDetalheResponse(
    Guid Id,
    string Numero,
    StatusOrdemServico Status,
    Guid ClienteId,
    Guid VeiculoId,
    Guid FilialId,
    string Observacoes,
    DateTime DataAbertura,
    DateTime? DataFechamento,
    IReadOnlyCollection<HistoricoStatusOSItem> Historico);

public record AcompanhamentoOSResponse(
    string Numero,
    StatusOrdemServico Status,
    DateTime DataAbertura,
    DateTime? DataFechamento,
    IReadOnlyCollection<HistoricoStatusOSItem> Historico);
