using System.Text;
using Microsoft.Extensions.Logging;
using OficinaMecanica.OS.Application.Contratos.Saga;
using OficinaMecanica.OS.Application.Ports.Out;

namespace OficinaMecanica.OS.Application.UseCases.Mensageria;

public enum StatusRecebimento
{
    Registrada,
    Duplicada,
    Rejeitada
}

public sealed record ResultadoRecebimento(StatusRecebimento Status, string? Motivo, Guid? MessageId, Guid? CorrelationId, string? MessageType);

// Valida a mensagem contra o contrato do canal e a registra na inbox, deduplicando por messageId (ADR-017).
// O efeito de negócio (transição da Saga e do status da OS) fica no CARD-40.
public class RegistrarMensagemRecebidaUseCase(IInboxRepository inbox, ILogger<RegistrarMensagemRecebidaUseCase> logger)
{
    public async Task<ResultadoRecebimento> ExecutarAsync(string canal, ReadOnlyMemory<byte> corpo, CancellationToken ct = default)
    {
        var contrato = CatalogoCanaisOS.Obter(canal);
        if (contrato is null)
            return new ResultadoRecebimento(StatusRecebimento.Rejeitada, $"Canal '{canal}' não é consumido pelo OS.", null, null, null);

        var leitura = LeitorMensagemSaga.Ler(contrato, corpo);
        if (!leitura.Valida)
            return new ResultadoRecebimento(StatusRecebimento.Rejeitada, leitura.Motivo, leitura.MessageId, leitura.CorrelationId, leitura.MessageType);

        var mensagem = leitura.Mensagem!;
        var nova = await inbox.RegistrarAsync(mensagem, canal, Encoding.UTF8.GetString(corpo.Span), ct);

        if (nova)
            logger.LogInformation("Mensagem registrada na inbox. MessageType={MessageType} OsId={OsId}", mensagem.MessageType, mensagem.OsId);
        else
            logger.LogInformation("Mensagem duplicada ignorada. MessageType={MessageType} OsId={OsId}", mensagem.MessageType, mensagem.OsId);

        return new ResultadoRecebimento(
            nova ? StatusRecebimento.Registrada : StatusRecebimento.Duplicada,
            null, mensagem.MessageId, mensagem.CorrelationId, mensagem.MessageType);
    }
}
