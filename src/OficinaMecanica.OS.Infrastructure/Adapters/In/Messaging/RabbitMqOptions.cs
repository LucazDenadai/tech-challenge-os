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
}
