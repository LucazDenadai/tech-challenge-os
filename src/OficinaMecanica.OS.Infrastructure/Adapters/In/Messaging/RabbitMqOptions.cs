using RabbitMQ.Client;

namespace OficinaMecanica.OS.Infrastructure.Adapters.In.Messaging;

public class RabbitMqOptions
{
    public const string Secao = "RabbitMq";

    public bool Enabled { get; set; }
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public ushort PrefetchCount { get; set; } = 10;
    // Intervalo de leitura da outbox pelo despachante.
    public int IntervaloOutboxMs { get; set; } = 500;

    public ConnectionFactory CriarFabrica(string nomeCliente) => new()
    {
        HostName = Host,
        Port = Port,
        VirtualHost = VirtualHost,
        UserName = Username,
        Password = Password,
        ClientProvidedName = nomeCliente,
        AutomaticRecoveryEnabled = true,
        TopologyRecoveryEnabled = true
    };
}
