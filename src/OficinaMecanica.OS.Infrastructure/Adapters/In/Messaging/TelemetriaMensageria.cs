using System.Diagnostics;

namespace OficinaMecanica.OS.Infrastructure.Adapters.In.Messaging;

public static class TelemetriaMensageria
{
    public const string NomeFonte = "OficinaMecanica.OS.Mensageria";

    public static readonly ActivitySource Fonte = new(NomeFonte);
}
