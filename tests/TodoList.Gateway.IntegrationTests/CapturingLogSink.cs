using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>
/// Sink do Serilog que só acumula a mensagem já renderizada de cada entrada — usado por
/// <see cref="GatewayApiFactory"/> para o teste de CA-26 (log de chamada de saída sem token/senha no
/// corpo). Entra no host por <c>ILogEventSink</c> no DI (o Serilog ignora <c>ILoggerProvider</c> extra).
/// As aspas que o Serilog põe em strings saem, para as asserções compararem o texto do template.
/// </summary>
public sealed class CapturingLogSink : ILogEventSink
{
    public ConcurrentQueue<string> Entries { get; } = new();

    public void Emit(LogEvent logEvent) => Entries.Enqueue(logEvent.RenderMessage().Replace("\"", string.Empty, StringComparison.Ordinal));
}
