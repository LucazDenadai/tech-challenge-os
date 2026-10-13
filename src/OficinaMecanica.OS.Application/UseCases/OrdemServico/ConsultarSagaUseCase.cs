using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Saga;

namespace OficinaMecanica.OS.Application.UseCases.OrdemServico;

public record TransicaoSagaItem(EstadoSaga? EstadoAnterior, EstadoSaga EstadoNovo, string Causa, Guid? MessageId, string? Motivo, DateTime Em);

// Referências dos participantes e histórico de transições. Sem dados do cliente: só IDs.
public record SagaOrdemServicoResponse(
    Guid CorrelationId,
    Guid OsId,
    EstadoSaga Estado,
    bool Terminada,
    string? Motivo,
    Guid? ExecutionId,
    Guid? QuoteId,
    int? QuoteVersion,
    decimal? ValorOrcamento,
    string? MoedaOrcamento,
    Guid? ReservationId,
    Guid? PaymentId,
    IReadOnlyList<TransicaoSagaItem> Transicoes);

// Estado da Saga lido só do banco do OS, sem consultar Billing nem Operações (ADR-017).
public class ConsultarSagaUseCase(ISagaRepository sagas)
{
    public async Task<SagaOrdemServicoResponse> ExecutarAsync(Guid osId, CancellationToken ct = default)
    {
        var saga = await sagas.ObterPorOsAsync(osId, ct) ?? throw new NotFoundException("Saga da OS", osId);

        return new SagaOrdemServicoResponse(saga.Id, saga.OsId, saga.Estado, saga.Terminada, saga.Motivo,
            saga.ExecutionId, saga.QuoteId, saga.QuoteVersion, saga.ValorOrcamento, saga.MoedaOrcamento,
            saga.ReservationId, saga.PaymentId,
            saga.Transicoes.OrderBy(t => t.Em)
                .Select(t => new TransicaoSagaItem(t.EstadoAnterior, t.EstadoNovo, t.Causa, t.MessageId, t.Motivo, t.Em))
                .ToList());
    }
}
