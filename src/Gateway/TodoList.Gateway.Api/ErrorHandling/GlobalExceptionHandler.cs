using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TodoList.Gateway.Api.Backends;

namespace TodoList.Gateway.Api.ErrorHandling;

/// <summary>
/// Handler global de exceções do Gateway (BE-36), mesmo padrão de
/// <c>TodoList.Tasks.Api.ErrorHandling.GlobalExceptionHandler</c>: toda
/// exceção não tratada por um handler mais específico vira <c>ProblemDetails</c>,
/// nunca stack trace/mensagem crua de transporte no corpo (CA-19). Três
/// caminhos além do genérico 500:
/// <list type="bullet">
/// <item><see cref="BadHttpRequestException"/> — corpo JSON malformado ou
/// enum de tipo forte desconhecido (não é o caso de <c>priority</c>/<c>dueDate</c>,
/// que trafegam como <c>string</c> de propósito) — 400, nunca 500 (CA-07);</item>
/// <item><see cref="BackendUnavailableException"/> — Identity ou Tasks fora
/// do ar/deadline — 503 com <c>Retry-After</c>, nunca 401 (D-28, CA-13/CA-24);</item>
/// <item><see cref="BackendCallException"/> — <c>RpcException</c> de negócio
/// do Tasks — traduzido por <see cref="GrpcErrorMapping"/> (D-35, CA-16 a
/// CA-18).</item>
/// </list>
/// </summary>
public sealed partial class GlobalExceptionHandler : IExceptionHandler
{
    private const int RetryAfterSeconds = GrpcErrorMapping.RetryAfterSeconds;

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

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case BadHttpRequestException badHttpRequestException:
                return await HandleBadHttpRequestAsync(httpContext, badHttpRequestException);

            case BackendUnavailableException unavailableException:
                return await HandleUnavailableAsync(httpContext, unavailableException);

            case BackendCallException callException:
                Log.BackendCallFailed(_logger, httpContext.TraceIdentifier, callException.StatusCode, callException);
                await callException.ToHttpResult().ExecuteAsync(httpContext);
                return true;

            default:
                return await HandleUnhandledAsync(httpContext, exception);
        }
    }

    private async ValueTask<bool> HandleUnhandledAsync(HttpContext httpContext, Exception exception)
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
                // CA-19: nunca a mensagem crua da exceção fora de Development.
                Detail = _environment.IsDevelopment()
                    ? exception.Message
                    : "Ocorreu um erro inesperado. Tente novamente mais tarde.",
            },
        });
    }

    private async ValueTask<bool> HandleBadHttpRequestAsync(HttpContext httpContext, BadHttpRequestException exception)
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

    private async ValueTask<bool> HandleUnavailableAsync(HttpContext httpContext, BackendUnavailableException exception)
    {
        Log.BackendUnavailable(_logger, httpContext.TraceIdentifier, exception.BackendName, exception);

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Serviço temporariamente indisponível.",
                Type = "https://httpstatuses.io/503",
                // CA-19: nunca o endereço interno do backend nem a mensagem crua do RpcException.
                Detail = "Não foi possível concluir a requisição no momento. Tente novamente em instantes.",
                Extensions = { ["errorCode"] = $"{exception.BackendName.ToLowerInvariant()}.unavailable" },
            },
        });
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Exceção não tratada. traceId={TraceId}")]
        public static partial void UnhandledException(ILogger logger, string traceId, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Requisição malformada. traceId={TraceId}, statusCode={StatusCode}")]
        public static partial void BadHttpRequest(ILogger logger, string traceId, int statusCode, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Backend indisponível. traceId={TraceId}, backend={Backend}")]
        public static partial void BackendUnavailable(ILogger logger, string traceId, string backend, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Chamada ao backend falhou. traceId={TraceId}, statusCode={StatusCode}")]
        public static partial void BackendCallFailed(ILogger logger, string traceId, Grpc.Core.StatusCode statusCode, Exception exception);
    }
}
