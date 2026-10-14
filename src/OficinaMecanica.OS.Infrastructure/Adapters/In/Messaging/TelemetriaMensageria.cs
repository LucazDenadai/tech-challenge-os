using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OficinaMecanica.OS.Infrastructure.Adapters.In.Messaging;

public static class TelemetriaMensageria
{
    public const string NomeFonte = "OficinaMecanica.OS.Mensageria";

    public static readonly ActivitySource Fonte = new(NomeFonte);

    // Métricas comuns da mensageria (CARD-42a), com o mesmo nome nos três serviços; o rótulo do scrape separa o serviço.
    public static readonly Meter Medidor = new(NomeFonte);

    private static readonly Counter<long> _publicacoes = Medidor.CreateCounter<long>(
        "outbox_publicacoes", description: "Publicações da outbox no RabbitMQ, por resultado.");

    private static readonly Counter<long> _dlq = Medidor.CreateCounter<long>(
        "mensagens_dlq", description: "Mensagens recebidas que foram para a DLQ, por canal de origem.");

    public static void Publicacao(bool sucesso)
        => _publicacoes.Add(1, new KeyValuePair<string, object?>("resultado", sucesso ? "sucesso" : "falha"));

    public static void Dlq(string canal) => _dlq.Add(1, new KeyValuePair<string, object?>("canal", canal));
}
