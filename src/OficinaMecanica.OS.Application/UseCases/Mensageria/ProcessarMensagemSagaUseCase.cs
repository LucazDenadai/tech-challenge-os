using System.Diagnostics.Metrics;
using System.Text;
using Microsoft.Extensions.Logging;
using OficinaMecanica.OS.Application.Contratos.Saga;
using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Saga;

namespace OficinaMecanica.OS.Application.UseCases.Mensageria;

public enum StatusRecebimento
{
    // A mensagem mudou o estado da Saga.
    Aplicada,
    // Válida, mas não se aplica ao estado atual (atrasada, de estado terminal ou de etapa do CARD-40b): nada muda.
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

    public static void Mensagem(string messageType, StatusRecebimento resultado)
        => _mensagens.Add(1, new KeyValuePair<string, object?>("message_type", messageType), new KeyValuePair<string, object?>("resultado", resultado.ToString()));
}

// Orquestrador da Saga (ADR-017, CARD-40a). Valida a mensagem contra o contrato do canal e, em uma transação do
// PostgreSQL do OS, registra a inbox, aplica a transição da Saga, atualiza status e histórico da OS e grava o
// próximo comando na outbox. Conflito de concorrência e falha de infraestrutura sobem como exceção: o consumidor
// refaz a mensagem do zero, com escopo novo.
public class ProcessarMensagemSagaUseCase(
    ITransacao transacao,
    IInboxRepository inbox,
    ISagaRepository sagas,
    IOrdemServicoRepository ordens,
    IClienteRepository clientes,
    IOutbox outbox,
    ILogger<ProcessarMensagemSagaUseCase> logger)
{
    private sealed record Efeito(bool Aplicada, MensagemSaga? Comando = null);

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
                var efeito = await AplicarAsync(saga, mensagem, ct);
                if (efeito.Aplicada)
                {
                    var os = await ordens.ObterPorIdAsync(saga.OsId, ct) ?? throw new NotFoundException("OS", saga.OsId);
                    StatusDaSaga.Sincronizar(os, saga);
                    await ordens.AtualizarAsync(os, ct);
                    if (efeito.Comando is { } comando)
                        outbox.Adicionar(comando);
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

    private async Task<Efeito> AplicarAsync(SagaOrdemServico saga, MensagemSaga mensagem, CancellationToken ct)
    {
        var causa = new Causa(mensagem.MessageType, mensagem.MessageId);
        var id = mensagem.MessageId;

        switch (mensagem)
        {
            case DiagnosisCompleted d:
                var itens = d.Items.Select(i => new ItemDiagnostico(
                    i.ItemId, Enum.Parse<TipoItemDiagnostico>(i.Type), i.Description, i.Quantity, i.UnitPrice));
                return saga.RegistrarDiagnostico(causa, d.ExecutionId, d.Currency, d.PriceSnapshotAtUtc, itens)
                    ? new Efeito(true, ComandoSaga.Orcamento(saga, id))
                    : new Efeito(false);

            case DiagnosisRejected r:
                return new Efeito(saga.RejeitarDiagnostico(causa, r.Reason));

            case QuoteReady q:
                return new Efeito(saga.RegistrarOrcamento(causa, q.QuoteId, q.QuoteVersion, q.Amount, q.Currency));

            case QuoteRejected r:
                return new Efeito(saga.RejeitarOrcamento(causa, r.Reason));

            case QuoteApproved q:
                if (!saga.AprovarOrcamento(causa, q.QuoteId, q.QuoteVersion))
                    return new Efeito(false);
                // Sem peças a Saga para em ação manual e não pede reserva.
                return new Efeito(true, saga.Estado == EstadoSaga.ReservingInventory ? ComandoSaga.Reserva(saga, id) : null);

            case QuoteDeclined q:
                return new Efeito(saga.EncerrarOrcamento(causa, q.QuoteId, q.QuoteVersion, "Orçamento recusado pelo cliente."));

            case QuoteExpired q:
                return new Efeito(saga.EncerrarOrcamento(causa, q.QuoteId, q.QuoteVersion, "Orçamento expirou sem decisão."));

            case InventoryReserved r:
                if (!saga.RegistrarReserva(causa, r.ReservationId))
                    return new Efeito(false);
                var cliente = await clientes.ObterPorIdAsync(saga.ClienteId, ct) ?? throw new NotFoundException("Cliente", saga.ClienteId);
                return new Efeito(true, ComandoSaga.Pagamento(saga, id, cliente.Email));

            case InventoryReservationRejected r:
                return new Efeito(saga.RejeitarReserva(causa, r.Reason));

            case PaymentPending p:
                return new Efeito(saga.RegistrarPagamentoPendente(causa, p.PaymentId));

            case PaymentApproved p:
                if (!saga.AprovarPagamento(causa, p.PaymentId, p.Amount, p.Currency))
                    return new Efeito(false);
                // Valor divergente para em ação manual: a execução não começa.
                return new Efeito(true, saga.Estado == EstadoSaga.StartingExecution ? ComandoSaga.InicioExecucao(saga, id) : null);

            case ExecutionStarted e:
                return new Efeito(saga.RegistrarInicioExecucao(causa, e.ExecutionId));

            case ExecutionCompleted e:
                return new Efeito(saga.ConcluirExecucao(causa, e.ExecutionId));

            default:
                // PaymentDeclined, PaymentOutcomeUnknown, ExecutionStartRejected, ExecutionFailed, InventoryReleased
                // e eventos de estorno: tratamento no CARD-40b.
                return new Efeito(false);
        }
    }
}

public class SagaDivergenteException(string mensagem) : Exception(mensagem);
