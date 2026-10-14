using System.Diagnostics.Metrics;
using System.Text;
using Microsoft.Extensions.Logging;
using OficinaMecanica.OS.Application.Contratos.Saga;
using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Application.UseCases.Saga;
using OficinaMecanica.OS.Domain.Saga;

namespace OficinaMecanica.OS.Application.UseCases.Mensageria;

public enum StatusRecebimento
{
    // A mensagem mudou o estado da Saga.
    Aplicada,
    // Válida, mas não se aplica ao estado atual (atrasada, repetida com outro messageId ou de estado terminal): nada muda.
    ForaDoFluxo,
    // messageId já registrado na inbox.
    Duplicada,
    // Fora do contrato ou sem Saga: vai para a DLQ com o motivo.
    Rejeitada
}

public sealed record ResultadoRecebimento(StatusRecebimento Status, string? Motivo, Guid? MessageId, Guid? CorrelationId, string? MessageType);

public static class MetricasSaga
{
    public const string NomeMedidor = "OficinaMecanica.OS.Saga";

    private static readonly Meter _medidor = new(NomeMedidor);
    private static readonly Counter<long> _mensagens = _medidor.CreateCounter<long>(
        "os_saga_mensagens", description: "Mensagens da Saga recebidas pelo OS, por tipo e resultado.");

    // Da abertura da OS ao estado terminal; os limites dos baldes estão na configuração do OpenTelemetry (CARD-42a).
    public const string NomeDuracao = "os_saga_duracao_segundos";

    private static readonly Histogram<double> _duracao = _medidor.CreateHistogram<double>(
        NomeDuracao, description: "Tempo da abertura da OS até o estado terminal da Saga, por estado final.");
    private static readonly Counter<long> _reenvios = _medidor.CreateCounter<long>(
        "os_saga_reenvios", description: "Reenvios de comandos por prazo vencido, por estado da Saga.");

    // Retrato dos estados não terminais, atualizado por uma leitura periódica do banco.
    private static volatile IReadOnlyCollection<EstadoAtual> _estados = [];

    public sealed record EstadoAtual(EstadoSaga Estado, int Quantidade, double IdadeEtapaMaisAntigaSegundos);

    static MetricasSaga()
    {
        _medidor.CreateObservableGauge("os_sagas_por_estado",
            () => _estados.Select(e => new Measurement<long>(e.Quantidade, Rotulo(e.Estado))),
            description: "Sagas em cada estado não terminal.");
        _medidor.CreateObservableGauge("os_saga_idade_etapa_segundos",
            () => _estados.Select(e => new Measurement<double>(e.IdadeEtapaMaisAntigaSegundos, Rotulo(e.Estado))),
            description: "Há quanto tempo está a etapa mais antiga em cada estado não terminal (0 sem Sagas no estado).");
    }

    public static void Mensagem(string messageType, StatusRecebimento resultado)
        => _mensagens.Add(1, new KeyValuePair<string, object?>("message_type", messageType), new KeyValuePair<string, object?>("resultado", resultado.ToString()));

    public static void Encerrada(EstadoSaga estadoFinal, TimeSpan duracao)
        => _duracao.Record(duracao.TotalSeconds, new KeyValuePair<string, object?>("estado_final", estadoFinal.ToString()));

    public static void Reenvio(EstadoSaga estado) => _reenvios.Add(1, Rotulo(estado));

    // Todos os estados não terminais aparecem, com zero quando vazios: o painel e os alertas não dependem de série ausente.
    public static IReadOnlyCollection<EstadoAtual> AtualizarEstados(
        IEnumerable<(EstadoSaga Estado, int Quantidade, DateTime EtapaMaisAntiga)> leitura, DateTime agora)
    {
        var porEstado = leitura.ToDictionary(l => l.Estado);
        return _estados = Enum.GetValues<EstadoSaga>()
            .Where(e => !SagaOrdemServico.EstadosTerminais.Contains(e))
            .Select(e => porEstado.TryGetValue(e, out var l)
                ? new EstadoAtual(e, l.Quantidade, Math.Max(0, (agora - l.EtapaMaisAntiga).TotalSeconds))
                : new EstadoAtual(e, 0, 0))
            .ToList();
    }

    private static KeyValuePair<string, object?> Rotulo(EstadoSaga estado) => new("estado", estado.ToString());
}

// Orquestrador da Saga (ADR-017, CARD-40a e CARD-40b). Valida a mensagem contra o contrato do canal e, em uma
// transação do PostgreSQL do OS, registra a inbox, aplica a transição da Saga, atualiza status e histórico da OS e
// grava na outbox os comandos da nova etapa. Conflito de concorrência e falha de infraestrutura sobem como exceção:
// o consumidor refaz a mensagem do zero, com escopo novo.
public class ProcessarMensagemSagaUseCase(
    ITransacao transacao,
    IInboxRepository inbox,
    ISagaRepository sagas,
    IOrdemServicoRepository ordens,
    ComandosSaga comandos,
    ILogger<ProcessarMensagemSagaUseCase> logger)
{
    // Aplicada: a mensagem mudou a Saga. Extras: comandos fora da regra "nova etapa -> ComandosDaEtapa",
    // usados quando um efeito chega depois do encerramento e precisa ser desfeito.
    private sealed record Efeito(bool Aplicada, IReadOnlyCollection<ComandoEtapa>? Extras = null);

    public async Task<ResultadoRecebimento> ExecutarAsync(string canal, ReadOnlyMemory<byte> corpo, CancellationToken ct = default)
    {
        var contrato = CatalogoCanaisOS.Obter(canal);
        if (contrato is null)
            return new ResultadoRecebimento(StatusRecebimento.Rejeitada, $"Canal '{canal}' não é consumido pelo OS.", null, null, null);

        var leitura = LeitorMensagemSaga.Ler(contrato, corpo);
        if (!leitura.Valida)
            return new ResultadoRecebimento(StatusRecebimento.Rejeitada, leitura.Motivo, leitura.MessageId, leitura.CorrelationId, leitura.MessageType);

        var mensagem = leitura.Mensagem!;
        StatusRecebimento status;
        try
        {
            var payload = Encoding.UTF8.GetString(corpo.Span);
            status = await transacao.ExecutarAsync(async () =>
            {
                if (!await inbox.RegistrarAsync(mensagem, canal, payload, ct))
                    return StatusRecebimento.Duplicada;

                var saga = await sagas.ObterPorOsAsync(mensagem.OsId, ct)
                    ?? throw new NotFoundException("Saga da OS", mensagem.OsId);
                if (saga.Id != mensagem.CorrelationId)
                    throw new SagaDivergenteException($"correlationId '{mensagem.CorrelationId}' não é o da Saga da OS '{mensagem.OsId}'.");

                var anterior = saga.Estado;
                var efeito = Aplicar(saga, mensagem);
                if (efeito.Aplicada)
                {
                    var os = await ordens.ObterPorIdAsync(saga.OsId, ct) ?? throw new NotFoundException("OS", saga.OsId);
                    StatusDaSaga.Sincronizar(os, saga);
                    await ordens.AtualizarAsync(os, ct);

                    // Nova etapa: os comandos dela. Mesma etapa (confirmação parcial, estorno pendente): nenhum.
                    var etapas = efeito.Extras ?? (saga.Estado != anterior ? saga.ComandosDaEtapa() : []);
                    await comandos.GravarAsync(saga, etapas, mensagem.MessageId, ct);
                    if (saga.Estado == EstadoSaga.ManualActionRequired && anterior != EstadoSaga.ManualActionRequired)
                    {
                        MetricasAcaoManual.Registrar(saga.CausaManual!.Value);
                        logger.LogError("Saga parada em ação manual. Causa={Causa} Motivo={Motivo} OsId={OsId}",
                            saga.CausaManual, saga.Motivo, saga.OsId);
                    }
                    logger.LogInformation("Saga {Anterior} → {Novo} por {MessageType}. OsId={OsId}",
                        anterior, saga.Estado, mensagem.MessageType, mensagem.OsId);
                }

                // A inbox fica registrada mesmo fora do fluxo: a mesma mensagem não é reavaliada.
                await ordens.SalvarAsync(ct);
                return efeito.Aplicada ? StatusRecebimento.Aplicada : StatusRecebimento.ForaDoFluxo;
            }, ct);
        }
        catch (Exception ex) when (ex is NotFoundException or SagaDivergenteException)
        {
            // Sem Saga para a mensagem não há o que tentar de novo: vai para a DLQ com o motivo.
            MetricasSaga.Mensagem(mensagem.MessageType, StatusRecebimento.Rejeitada);
            return new ResultadoRecebimento(StatusRecebimento.Rejeitada, ex.Message, mensagem.MessageId, mensagem.CorrelationId, mensagem.MessageType);
        }

        if (status == StatusRecebimento.ForaDoFluxo)
            logger.LogWarning("Mensagem fora do fluxo da Saga; nada mudou. MessageType={MessageType} OsId={OsId}", mensagem.MessageType, mensagem.OsId);
        else if (status == StatusRecebimento.Duplicada)
            logger.LogInformation("Mensagem duplicada ignorada. MessageType={MessageType} OsId={OsId}", mensagem.MessageType, mensagem.OsId);

        MetricasSaga.Mensagem(mensagem.MessageType, status);
        return new ResultadoRecebimento(status, null, mensagem.MessageId, mensagem.CorrelationId, mensagem.MessageType);
    }

    private static Efeito Aplicar(SagaOrdemServico saga, MensagemSaga mensagem)
    {
        var c = new Causa(mensagem.MessageType, mensagem.MessageId);

        return mensagem switch
        {
            DiagnosisCompleted d => new Efeito(saga.RegistrarDiagnostico(c, d.ExecutionId, d.Currency, d.PriceSnapshotAtUtc,
                d.Items.Select(i => new ItemDiagnostico(i.ItemId, Enum.Parse<TipoItemDiagnostico>(i.Type), i.Description, i.Quantity, i.UnitPrice)))),
            DiagnosisRejected r => new Efeito(saga.RejeitarDiagnostico(c, r.Reason)),

            QuoteReady q => new Efeito(saga.RegistrarOrcamento(c, q.QuoteId, q.QuoteVersion, q.Amount, q.Currency)),
            QuoteRejected r => new Efeito(saga.RejeitarOrcamento(c, r.Reason)),
            QuoteApproved q => new Efeito(saga.AprovarOrcamento(c, q.QuoteId, q.QuoteVersion)),
            QuoteDeclined q => new Efeito(saga.EncerrarOrcamento(c, q.QuoteId, q.QuoteVersion, "Orçamento recusado pelo cliente.")),
            QuoteExpired q => new Efeito(saga.EncerrarOrcamento(c, q.QuoteId, q.QuoteVersion, "Orçamento expirou sem decisão.")),

            // Reserva ou pagamento confirmados depois que a Saga terminou: desfazer para não prender estoque nem dinheiro.
            InventoryReserved r when saga.Terminada =>
                new Efeito(saga.RegistrarReservaAposEncerramento(c, r.ReservationId), [ComandoEtapa.Liberacao]),
            InventoryReserved r => new Efeito(saga.RegistrarReserva(c, r.ReservationId)),
            InventoryReservationRejected r => new Efeito(saga.RejeitarReserva(c, r.Reason)),
            InventoryReleased r => new Efeito(saga.RegistrarLiberacao(c, r.ReservationId)),

            PaymentApproved p when saga.Terminada =>
                new Efeito(saga.RegistrarPagamentoAposEncerramento(c, p.PaymentId), [ComandoEtapa.Estorno]),
            PaymentPending p => new Efeito(saga.RegistrarPagamentoPendente(c, p.PaymentId)),
            PaymentApproved p => new Efeito(saga.AprovarPagamento(c, p.PaymentId, p.Amount, p.Currency)),
            PaymentDeclined p => new Efeito(saga.RecusarPagamento(c, p.PaymentId, p.Reason)),
            PaymentOutcomeUnknown p => new Efeito(saga.RegistrarResultadoDesconhecido(c, p.PaymentId, p.Reason)),

            ExecutionStarted e => new Efeito(saga.RegistrarInicioExecucao(c, e.ExecutionId)),
            ExecutionStartRejected e => new Efeito(saga.RejeitarInicioExecucao(c, e.ExecutionId, e.Reason)),
            ExecutionCompleted e => new Efeito(saga.ConcluirExecucao(c, e.ExecutionId)),
            ExecutionFailed e => new Efeito(saga.FalharExecucao(c, e.ExecutionId, e.Reason)),
            ExecutionCancelled e => new Efeito(saga.RegistrarCancelamentoExecucao(c, e.ExecutionId)),
            ExecutionCancelRejected e => new Efeito(saga.RecusarCancelamentoExecucao(c, e.ExecutionId, e.Reason)),

            PaymentRefunded r => new Efeito(saga.RegistrarEstorno(c, r.PaymentId)),
            PaymentRefundPending r => new Efeito(saga.RegistrarEstornoPendente(c, r.PaymentId)),
            PaymentRefundFailed r => new Efeito(saga.FalharEstorno(c, r.PaymentId, r.Reason)),

            _ => new Efeito(false)
        };
    }
}

public class SagaDivergenteException(string mensagem) : Exception(mensagem);
