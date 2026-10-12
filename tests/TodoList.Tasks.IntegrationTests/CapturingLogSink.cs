using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace TodoList.Tasks.IntegrationTests;

/// <summary>
/// Sink do Serilog que acumula nível e mensagem renderizada de cada entrada. Registrado no DI do host
/// (<c>services.AddSingleton&lt;ILogEventSink&gt;(sink)</c>) — é como os testes enxergam o que o log
/// estruturado (<c>StructuredLogging.AddStructuredLogging</c>) escreveria no console. O Serilog renderiza
/// strings entre aspas; elas saem aqui para os testes compararem o texto do template (<c>userId=...</c>).
/// </summary>
public sealed class CapturingLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyCollection<(LogLevel Level, string Message)> Entries => _entries;

    public IReadOnlyCollection<string> Messages => _entries.Select(entry => entry.Message).ToList();

    /// <summary>Os eventos crus, para asserções sobre propriedades (<c>userId</c>, <c>service</c>...).</summary>
    public IReadOnlyCollection<LogEvent> Events => _events;

    public void Emit(LogEvent logEvent)
    {
        _events.Enqueue(logEvent);
        _entries.Enqueue(((LogLevel)(int)logEvent.Level, logEvent.RenderMessage().Replace("\"", string.Empty, StringComparison.Ordinal)));
    }
}
