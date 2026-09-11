using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>
/// <see cref="ILoggerProvider"/> mínimo que só acumula a mensagem já formatada
/// de cada entrada de log — usado por <see cref="GatewayApiFactory"/> para o
/// teste de CA-26 (log de chamada de saída sem token/senha no corpo).
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly ConcurrentQueue<string> _entries;

        public CapturingLogger(ConcurrentQueue<string> entries)
        {
            _entries = entries;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _entries.Enqueue(formatter(state, exception));
        }
    }
}
