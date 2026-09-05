using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TodoList.Identity.Api.ErrorHandling;

/// <summary>
/// Handler global de exceções (BE-03, CA-05): qualquer exceção não tratada
/// por um handler vira 500 com <c>ProblemDetails</c> genérico. Nunca expõe
/// nome de tipo, stack trace ou mensagem da exceção original fora de
/// <c>Development</c> — a mensagem é sempre a mesma frase fixa.
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

    private static partial class Log
    {
        // traceId explícito no template (e não só via scope de hosting) para que
        // CA-06 (traceId da resposta == traceId do log) seja verificável sem
        // depender do formato interno do scope padrão do ASP.NET Core.
        [LoggerMessage(Level = LogLevel.Error, Message = "Exceção não tratada. traceId={TraceId}")]
        public static partial void UnhandledException(ILogger logger, string traceId, Exception exception);
    }
}
