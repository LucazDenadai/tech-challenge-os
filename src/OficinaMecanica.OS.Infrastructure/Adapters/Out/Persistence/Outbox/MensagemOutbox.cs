namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Outbox;

// Comando a publicar, gravado na mesma transação da transição da Saga (ADR-016). O despachante publica e marca.
public class MensagemOutbox
{
    public Guid MessageId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string Canal { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public string Payload { get; set; } = string.Empty;
    // Contexto W3C do trace que gerou o comando, para o consumidor continuar o mesmo trace.
    public string? TraceParent { get; set; }
    public DateTimeOffset CriadaEmUtc { get; set; }
    public DateTimeOffset? PublicadaEmUtc { get; set; }
    public int Tentativas { get; set; }
    public string? UltimoErro { get; set; }
}
