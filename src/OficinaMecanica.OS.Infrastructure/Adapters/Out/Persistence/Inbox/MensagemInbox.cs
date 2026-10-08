using System.Diagnostics.CodeAnalysis;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Inbox;

// Registro de mensagem recebida, chave de deduplicação por messageId (ADR-017).
// Modela a tabela para as migrations; a gravação é feita por INSERT ... ON CONFLICT no InboxRepository.
[ExcludeFromCodeCoverage]
public class MensagemInbox
{
    public Guid MessageId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string Canal { get; set; } = string.Empty;
    public string Producer { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public Guid OsId { get; set; }
    public Guid FilialId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset RecebidaEmUtc { get; set; }
    public string Payload { get; set; } = string.Empty;
}
