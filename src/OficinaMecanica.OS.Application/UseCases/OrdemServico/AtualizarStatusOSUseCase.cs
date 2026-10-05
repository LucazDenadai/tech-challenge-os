using Microsoft.Extensions.Logging;
using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Application.UseCases.OrdemServico;

public class AtualizarStatusOSUseCase(
    IOrdemServicoRepository repository,
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
