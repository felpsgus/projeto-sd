using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TodoList.Tasks.Api.ErrorHandling;

/// <summary>
/// Handler global de exceções (BE-03, CA-05): qualquer exceção não tratada
/// por um handler vira 500 com <c>ProblemDetails</c> genérico. Nunca expõe
/// nome de tipo, stack trace ou mensagem da exceção original fora de
/// <c>Development</c> — a mensagem é sempre a mesma frase fixa.
///
/// <para>
/// <b>Exceção à regra "tudo vira 500" — <see cref="BadHttpRequestException"/>
/// (BE-17, CA-05/CA-13):</b> o binding automático de Minimal API para o corpo
/// JSON (ex.: <c>priority</c> com um valor de enum inexistente, <c>dueDate</c>
/// em formato inválido) lança essa exceção, que já carrega o status HTTP
/// correto em <see cref="BadHttpRequestException.StatusCode"/> (400) —
/// tratá-la como qualquer outra exceção a rebaixaria para 500, que é
/// exatamente o que BE-17 CA-05/CA-13 proíbem ("retorna 400, não 500").
/// Nenhum outro handler global (a mensagem de <c>JsonException</c> interna
/// pode conter nome de tipo/caminho do parser) — a mensagem devolvida é fixa,
/// sem o detalhe cru do parser.
/// </para>
/// </summary>
public sealed partial class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetailsService,
        IHostEnvironment environment,
        ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _environment = environment;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is BadHttpRequestException badHttpRequestException)
        {
            return await TryHandleBadHttpRequestAsync(httpContext, badHttpRequestException);
        }

        Log.UnhandledException(_logger, httpContext.TraceIdentifier, exception);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Erro interno inesperado.",
                Type = "https://httpstatuses.io/500",
                Detail = _environment.IsDevelopment()
                    ? exception.Message
                    : "Ocorreu um erro inesperado. Tente novamente mais tarde.",
            },
        });
    }

    private async ValueTask<bool> TryHandleBadHttpRequestAsync(HttpContext httpContext, BadHttpRequestException exception)
    {
        Log.BadHttpRequest(_logger, httpContext.TraceIdentifier, exception.StatusCode, exception);

        httpContext.Response.StatusCode = exception.StatusCode;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = exception.StatusCode,
                Title = "Requisição malformada.",
                Type = $"https://httpstatuses.io/{exception.StatusCode}",
                Detail = "O corpo da requisição não pôde ser interpretado — verifique o formato dos campos enviados.",
            },
        });
    }

    private static partial class Log
    {
        // traceId explícito no template (e não só via scope de hosting) para que
        // CA-06 (traceId da resposta == traceId do log) seja verificável sem
        // depender do formato interno do scope padrão do ASP.NET Core.
        [LoggerMessage(Level = LogLevel.Error, Message = "Exceção não tratada. traceId={TraceId}")]
        public static partial void UnhandledException(ILogger logger, string traceId, Exception exception);

        // Warning, não Error: é entrada malformada do cliente, não bug do
        // servidor (BE-17, CA-05/CA-13).
        [LoggerMessage(Level = LogLevel.Warning, Message = "Requisição malformada. traceId={TraceId}, statusCode={StatusCode}")]
        public static partial void BadHttpRequest(ILogger logger, string traceId, int statusCode, Exception exception);
    }
}
