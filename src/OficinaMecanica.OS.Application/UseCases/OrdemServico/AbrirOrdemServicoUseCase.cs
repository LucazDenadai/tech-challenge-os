using Microsoft.Extensions.Logging;
using OficinaMecanica.OS.Application.Contratos.Saga;
using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Saga;
using DomainCliente = OficinaMecanica.OS.Domain.Entities.Cliente;
using DomainOS = OficinaMecanica.OS.Domain.Entities.OrdemServico;

namespace OficinaMecanica.OS.Application.UseCases.OrdemServico;

// Abre a OS e inicia a Saga (ADR-017): OS, Saga em Diagnosing e DiagnosisRequested na outbox
// são gravados no mesmo SaveChanges, então ou tudo existe, ou nada.
public class AbrirOrdemServicoUseCase(
    IOrdemServicoRepository osRepository,
    IVeiculoRepository veiculoRepository,
    IFilialRepository filialRepository,
    IClienteRepository clienteRepository,
    ISagaRepository sagaRepository,
    IOutbox outbox,
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

        // O pagamento da Saga precisa do e-mail do cliente (payerEmail, ADR-019). Recusar aqui evita
        // que a Saga pare no meio, depois de reservar estoque.
        var cliente = await clienteRepository.ObterPorIdAsync(request.ClienteId, ct)
            ?? throw new NotFoundException("Cliente", request.ClienteId);

        if (!DomainCliente.EmailValido(cliente.Email))
            throw new InvalidOperationException("O cliente não tem e-mail válido. Atualize o cadastro antes de abrir a OS.");

        var numero = await osRepository.GerarNumeroAsync(ct);
        var os = new DomainOS(numero, request.ClienteId, request.VeiculoId, request.FilialId, request.Observacoes ?? string.Empty);
        var saga = SagaOrdemServico.Iniciar(os);

        await osRepository.AdicionarAsync(os, ct);
        await sagaRepository.AdicionarAsync(saga, ct);
        outbox.Adicionar(ComandoSaga.Diagnostico(saga));
        await osRepository.SalvarAsync(ct);

        logger.LogInformation("OS criada e Saga iniciada. OrdemServicoId={OrdemServicoId} CorrelationId={CorrelationId} FilialId={FilialId}",
            os.Id, saga.Id, request.FilialId);

        return new AbrirOrdemServicoResponse(os.Id, os.Numero);
    }
}
