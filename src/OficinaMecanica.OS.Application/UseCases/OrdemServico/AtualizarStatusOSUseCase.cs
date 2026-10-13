using Microsoft.Extensions.Logging;
using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Application.UseCases.OrdemServico;

// Transições manuais da API (CARD-37b): entrega e cancelamento. O cancelamento também encerra a Saga, na mesma
// gravação, para que eventos atrasados dos participantes não mexam mais na OS. Liberar a reserva e estornar o
// pagamento pela Saga fica no CARD-40b.
public class AtualizarStatusOSUseCase(
    IOrdemServicoRepository repository,
    ISagaRepository sagaRepository,
    IEmailPort emailPort,
    IClienteRepository clienteRepository,
    ILogger<AtualizarStatusOSUseCase> logger)
{
    public async Task ExecutarAsync(Guid osId, StatusOrdemServico novoStatus, CancellationToken ct = default)
    {
        var os = await repository.ObterComDetalhesAsync(osId, ct)
            ?? throw new NotFoundException("OrdemServico", osId);

        var statusAnterior = os.Status;
        os.AlterarStatus(novoStatus);

        if (novoStatus == StatusOrdemServico.Cancelada
            && await sagaRepository.ObterPorOsAsync(osId, ct) is { } saga
            && saga.CancelarManualmente("OS cancelada pelo atendimento."))
            logger.LogWarning("Saga encerrada por cancelamento manual, sem compensação automática. OrdemServicoId={OrdemServicoId} CorrelationId={CorrelationId}",
                osId, saga.Id);

        await repository.AtualizarAsync(os, ct);
        await repository.SalvarAsync(ct);

        logger.LogInformation("Status de OS alterado. OrdemServicoId={OrdemServicoId} StatusAnterior={StatusAnterior} NovoStatus={NovoStatus}",
            osId, statusAnterior, novoStatus);

        var cliente = await clienteRepository.ObterPorIdAsync(os.ClienteId, ct);
        if (cliente is not null)
        {
            try { await emailPort.EnviarAtualizacaoStatusAsync(cliente.Email, os.Numero, novoStatus, ct); }
            catch { /* falha no SMTP não bloqueia a transição de status */ }
        }
    }
}
