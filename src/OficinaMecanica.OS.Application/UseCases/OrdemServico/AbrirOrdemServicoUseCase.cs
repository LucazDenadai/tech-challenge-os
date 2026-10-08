using Microsoft.Extensions.Logging;
using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using DomainOS = OficinaMecanica.OS.Domain.Entities.OrdemServico;

namespace OficinaMecanica.OS.Application.UseCases.OrdemServico;

public class AbrirOrdemServicoUseCase(
    IOrdemServicoRepository osRepository,
    IVeiculoRepository veiculoRepository,
    IFilialRepository filialRepository,
    ILogger<AbrirOrdemServicoUseCase> logger)
{
    public async Task<AbrirOrdemServicoResponse> ExecutarAsync(AbrirOrdemServicoRequest request, CancellationToken ct = default)
    {
        var veiculo = await veiculoRepository.ObterPorIdAsync(request.VeiculoId, ct)
            ?? throw new NotFoundException("Veiculo", request.VeiculoId);

        if (veiculo.ClienteId != request.ClienteId)
            throw new InvalidOperationException("O veículo não pertence ao cliente informado.");

        var filial = await filialRepository.ObterPorIdAsync(request.FilialId, ct)
            ?? throw new NotFoundException("Filial", request.FilialId);

        if (!filial.Ativo)
            throw new InvalidOperationException("A filial informada está inativa.");

        var numero = await osRepository.GerarNumeroAsync(ct);
        var os = new DomainOS(numero, request.ClienteId, request.VeiculoId, request.FilialId, request.Observacoes ?? string.Empty);

        await osRepository.AdicionarAsync(os, ct);
        await osRepository.SalvarAsync(ct);

        logger.LogInformation("OS criada com sucesso. OrdemServicoId={OrdemServicoId} VeiculoId={VeiculoId} FilialId={FilialId}",
            os.Id, request.VeiculoId, request.FilialId);

        return new AbrirOrdemServicoResponse(os.Id, os.Numero);
    }
}
