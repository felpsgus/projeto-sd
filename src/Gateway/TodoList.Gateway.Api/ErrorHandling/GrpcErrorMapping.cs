using System.Globalization;
using System.Text.Json;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TodoList.Gateway.Api.Backends;

namespace TodoList.Gateway.Api.ErrorHandling;

/// <summary>
/// Mapeamento <see cref="StatusCode"/> gRPC → HTTP (BE-36, D-35) — a mesma
/// tabela de <c>ResultGrpcStatus</c>/<c>ResultHttpResults</c> do Tasks Service,
/// agora do lado do Gateway, traduzindo o <see cref="BackendCallException"/>
/// levantado por <see cref="Backends.TasksBackend"/>. O <c>error-code</c> do
/// trailer é preservado em <c>extensions.errorCode</c> — o contrato de erro
/// que o front já consome de Identity/Tasks hoje não muda com a introdução do
/// Gateway.
/// </summary>
public static class GrpcErrorMapping
{
    /// <summary>Trailer com o código estável do catálogo de erros (D-35).</summary>
    public const string ErrorCodeTrailerKey = "error-code";

    /// <summary>Trailer com o JSON <c>{ "Campo": ["mensagem", ...] }</c> de uma falha de validação (BE-35 CA-03).</summary>
    public const string ValidationErrorsTrailerKey = "validation-errors";

    /// <summary>Segundos sugeridos no <c>Retry-After</c> de um 503 originado de um backend (D-28).</summary>
    public const int RetryAfterSeconds = 5;

    /// <summary>Converte um <see cref="BackendCallException"/> na resposta HTTP correspondente (D-35).</summary>
    public static IResult ToHttpResult(this BackendCallException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var errorCode = exception.Trailers.GetValue(ErrorCodeTrailerKey);
        var validationErrorsJson = exception.Trailers.GetValue(ValidationErrorsTrailerKey);

        return ToHttpResult(exception.StatusCode, errorCode, validationErrorsJson, exception.Message);
    }

    /// <summary>
    /// Núcleo puro do mapeamento (testável sem <see cref="RpcException"/> ou
    /// <see cref="Metadata"/> nenhuma) — cobre todo <see cref="StatusCode"/>
    /// da tabela de D-35.
    /// </summary>
    public static IResult ToHttpResult(StatusCode statusCode, string? errorCode, string? validationErrorsJson, string detail) =>
        statusCode switch
        {
            StatusCode.InvalidArgument => ToValidationProblem(validationErrorsJson, errorCode),
            StatusCode.NotFound => ToProblem(StatusCodes.Status404NotFound, "Recurso não encontrado.", detail, errorCode),
            StatusCode.FailedPrecondition => ToProblem(StatusCodes.Status409Conflict, "Conflito de estado.", detail, errorCode),
            StatusCode.Unauthenticated => ToProblem(StatusCodes.Status401Unauthorized, "Não autenticado.", detail, errorCode),
            StatusCode.Unavailable or StatusCode.DeadlineExceeded => ToUnavailableProblem(errorCode),
            // CA-19: qualquer outro status (Internal, etc.) vira 500 genérico
            // — nunca a mensagem crua de transporte do RpcException original.
            _ => ToProblem(
                StatusCodes.Status500InternalServerError,
                "Erro interno inesperado.",
                "Ocorreu um erro inesperado ao processar a requisição.",
                errorCode),
        };

    private static IResult ToValidationProblem(string? validationErrorsJson, string? errorCode)
    {
        var errors = validationErrorsJson is not null
            ? JsonSerializer.Deserialize<Dictionary<string, string[]>>(validationErrorsJson) ?? []
            : [];

        return Results.ValidationProblem(errors, statusCode: StatusCodes.Status400BadRequest, extensions: ErrorCodeExtensions(errorCode));
    }

    private static IResult ToProblem(int statusCode, string title, string detail, string? errorCode) =>
        Results.Problem(
            statusCode: statusCode,
            title: title,
            type: $"https://httpstatuses.io/{statusCode}",
            detail: detail,
            extensions: ErrorCodeExtensions(errorCode));

    private static ResultWithRetryAfter ToUnavailableProblem(string? errorCode)
    {
        var problem = Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Serviço temporariamente indisponível.",
            type: "https://httpstatuses.io/503",
            detail: "Não foi possível concluir a requisição no momento. Tente novamente em instantes.",
            extensions: ErrorCodeExtensions(errorCode));

        return new ResultWithRetryAfter(problem, RetryAfterSeconds);
    }

    private static Dictionary<string, object?> ErrorCodeExtensions(string? errorCode) =>
        errorCode is null ? [] : new Dictionary<string, object?> { ["errorCode"] = errorCode };

    /// <summary>
    /// Envolve um <see cref="IResult"/> já pronto só para acrescentar o
    /// cabeçalho <c>Retry-After</c> — <c>Results.Problem</c> não tem
    /// parâmetro para cabeçalhos arbitrários (mesmo padrão de
    /// <c>ResultHttpResults</c> do Tasks Service).
    /// </summary>
    private sealed class ResultWithRetryAfter : IResult
    {
        private readonly IResult _inner;
        private readonly int _retryAfterSeconds;

        public ResultWithRetryAfter(IResult inner, int retryAfterSeconds)
        {
            _inner = inner;
            _retryAfterSeconds = retryAfterSeconds;
        }

        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.RetryAfter = _retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

            return _inner.ExecuteAsync(httpContext);
        }
    }
}
