using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace OficinaMecanica.OS.IntegrationTests.Fixtures;

// Provedor de log que guarda cada mensagem com os valores dos escopos ativos, para os testes verificarem o que um
// log leva além do texto (CARD-42b).
public sealed class LogsCapturados : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<LogCapturado> _logs = new();
    private IExternalScopeProvider _escopos = new LoggerExternalScopeProvider();

    public sealed record LogCapturado(string Mensagem, IReadOnlyDictionary<string, string?> Escopos)
    {
        public string? Escopo(string chave) => Escopos.GetValueOrDefault(chave);
    }

    public bool Contem(string trecho) => _logs.Any(l => l.Mensagem.Contains(trecho));

    public LogCapturado Primeiro(string trecho) => _logs.First(l => l.Mensagem.Contains(trecho));

    public ILogger CreateLogger(string categoryName) => new Registrador(this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _escopos = scopeProvider;

    public void Dispose() { }

    private sealed class Registrador(LogsCapturados dono) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => dono._escopos.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var escopos = new Dictionary<string, string?>();
            dono._escopos.ForEachScope((escopo, destino) =>
            {
                if (escopo is IEnumerable<KeyValuePair<string, object?>> pares)
                    foreach (var (chave, valor) in pares)
                        destino[chave] = valor?.ToString();
            }, escopos);
            dono._logs.Enqueue(new LogCapturado(formatter(state, exception), escopos));
        }
    }
}
