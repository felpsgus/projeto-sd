using Microsoft.Extensions.Logging;

namespace TodoList.Tasks.Api.Startup;

/// <summary>
/// Log estruturado emitido durante a inicialização (fora do hot path de
/// requisição, mas ainda via <c>LoggerMessage</c> source-gen — mesma
/// convenção do resto do serviço, evita CA1848).
/// </summary>
internal static partial class StartupLog
{
    /// <summary>
    /// BE-29, CA-06: aviso explícito de que o serviço está aceitando o dono
    /// da tarefa por header, sem autenticação — para que o modo provisório
    /// nunca suba "por esquecimento" sem que alguém veja.
    /// </summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Tasks:AllowAnonymousCreate=true — POST /api/tasks aceita o dono da tarefa pelo header " +
            "'X-User-Id', sem autenticação (BE-29, D-30). Risco assumido e delimitado: aceitável em ambiente " +
            "local/demo, NUNCA em ambiente exposto.")]
    public static partial void AllowAnonymousCreateEnabled(ILogger logger);
}
