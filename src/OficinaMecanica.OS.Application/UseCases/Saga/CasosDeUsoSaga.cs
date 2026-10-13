using Microsoft.Extensions.Logging;
using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Saga;

namespace OficinaMecanica.OS.Application.UseCases.Saga;

// Grava uma mudança da Saga que não veio de mensagem (API ou prazo): status da OS, comandos da nova etapa e
// histórico no mesmo SaveChanges.
public class GravacaoSaga(IOrdemServicoRepository ordens, ComandosSaga comandos)
{
    public async Task GravarAsync(SagaOrdemServico saga, EstadoSaga anterior, CancellationToken ct, IReadOnlyCollection<ComandoEtapa>? etapas = null)
    {
        var os = await ordens.ObterPorIdAsync(saga.OsId, ct) ?? throw new NotFoundException("OS", saga.OsId);
        StatusDaSaga.Sincronizar(os, saga);
        await ordens.AtualizarAsync(os, ct);
        await comandos.GravarAsync(saga, etapas ?? (saga.Estado != anterior ? saga.ComandosDaEtapa() : []), causationId: null, ct);
        await ordens.SalvarAsync(ct);
    }
}

// Cancelamento manual da OS pela Saga (ADR-020). O OS não sabe se o reparo começou: em execução, o
// cancelamento vira pedido a Operações. A resposta da rota diz se acabou (204) ou está em andamento (202).
public class CancelarOrdemServicoUseCase(ISagaRepository sagas, GravacaoSaga gravacao, ILogger<CancelarOrdemServicoUseCase> logger)
{
    public async Task<ResultadoCancelamento> ExecutarAsync(Guid osId, CancellationToken ct = default)
    {
        var saga = await sagas.ObterPorOsAsync(osId, ct) ?? throw new NotFoundException("OrdemServico", osId);
        var anterior = saga.Estado;

        var resultado = saga.SolicitarCancelamento("OS cancelada pelo atendimento.");
        if (resultado == ResultadoCancelamento.Recusado)
            throw new InvalidOperationException(saga.Estado == EstadoSaga.ManualActionRequired
                ? "A Saga da OS espera ação manual: use a resolução do Admin."
                : $"OS no estado '{saga.Estado}' não pode ser cancelada.");

        await gravacao.GravarAsync(saga, anterior, ct);
        logger.LogInformation("Cancelamento da OS {Resultado}. OsId={OsId} Saga {Anterior} → {Novo}", resultado, osId, anterior, saga.Estado);
        return resultado;
    }
}

// Resolução de ManualActionRequired pelo Admin (ADR-020): cancelar com compensação ou retomar a etapa.
public class ResolverAcaoManualUseCase(ISagaRepository sagas, GravacaoSaga gravacao, ILogger<ResolverAcaoManualUseCase> logger)
{
    public async Task ExecutarAsync(Guid osId, AcaoResolucao acao, string motivo, string usuario, CancellationToken ct = default)
    {
        var saga = await sagas.ObterPorOsAsync(osId, ct) ?? throw new NotFoundException("Saga da OS", osId);
        var anterior = saga.Estado;
        var causa = saga.CausaManual;

        saga.ResolverAcaoManual(acao, $"{usuario}: {motivo?.Trim()}");

        // A retomada reenvia os comandos da etapa mesmo quando ela é igual à anterior à ação manual.
        await gravacao.GravarAsync(saga, anterior, ct, saga.ComandosDaEtapa());
        logger.LogWarning("Ação manual resolvida por {Usuario}: {Acao}. Causa={Causa} OsId={OsId} Saga → {Novo}",
            usuario, acao, causa, osId, saga.Estado);
    }
}

// Prazo das etapas que esperam um participante (CARD-40b): reenvia os comandos com a mesma idempotencyKey até
// MaxReenvios; depois, ação manual com alerta. Cada Saga é tratada em escopo próprio pelo serviço de agendamento.
public class VerificarPrazoSagaUseCase(ISagaRepository sagas, GravacaoSaga gravacao, ConfiguracaoSaga configuracao,
    ILogger<VerificarPrazoSagaUseCase> logger)
{
    public Task<IReadOnlyList<Guid>> ListarVencidasAsync(DateTime agora, CancellationToken ct = default)
        => sagas.ListarComPrazoVencidoAsync(agora - configuracao.Prazo, 100, ct);

    public async Task ExecutarAsync(Guid sagaId, DateTime agora, CancellationToken ct = default)
    {
        var saga = await sagas.ObterAsync(sagaId, ct);
        if (saga is null || !saga.PrazoVencido(agora, configuracao.Prazo))
            return;

        var anterior = saga.Estado;
        if (saga.Reenvios < configuracao.MaxReenvios)
        {
            saga.RegistrarReenvio();
            await gravacao.GravarAsync(saga, anterior, ct, saga.ComandosDaEtapa());
            logger.LogWarning("Prazo vencido em {Estado}; comandos reenviados ({Reenvios}). OsId={OsId}", anterior, saga.Reenvios, saga.OsId);
            return;
        }

        saga.EsgotarPrazo();
        await gravacao.GravarAsync(saga, anterior, ct);
        MetricasAcaoManual.Registrar(CausaAcaoManual.PrazoEsgotado);
        logger.LogError("Saga parada em ação manual: sem resposta em {Estado} depois de {Reenvios} reenvios. OsId={OsId}",
            anterior, configuracao.MaxReenvios, saga.OsId);
    }
}
