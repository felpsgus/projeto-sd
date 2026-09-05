using Microsoft.AspNetCore.Http;
using TodoList.SharedKernel;

namespace TodoList.Tasks.Api.ResultMapping;

/// <summary>
/// Extensão que converte <see cref="Result"/>/<see cref="Result{TValue}"/> em
/// <see cref="IResult"/> de Minimal API (BE-03) — endpoints não precisam
/// escrever o <c>switch</c> de <see cref="ErrorType"/> para status HTTP
/// (CA-08). Mora aqui, e não em <c>TodoList.SharedKernel</c>, porque depende
/// de <c>Microsoft.AspNetCore.Http</c> — o <c>SharedKernel</c> não pode
/// arrastar esse pacote (D-26, CA-10). Duplicada em
/// <c>TodoList.Identity.Api</c>: são poucas linhas, e o preço de compartilhar
/// seria acoplar os dois serviços por um projeto extra.
/// </summary>
public static class ResultHttpResults
{
    /// <summary>Converte um <see cref="Result"/> sem valor em <see cref="IResult"/>.</summary>
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Microsoft.AspNetCore.Http.Results.Ok() : ToProblem(result.Error);

    /// <summary>Converte um <see cref="Result{TValue}"/> em <see cref="IResult"/>.</summary>
    public static IResult ToHttpResult<TValue>(this Result<TValue> result) =>
        result.IsSuccess ? Microsoft.AspNetCore.Http.Results.Ok(result.Value) : ToProblem(result.Error);

    /// <summary>
    /// Segundos sugeridos no <c>Retry-After</c> de uma resposta <c>503</c>
    /// (BE-28, CA do cabeçalho) — maior que o deadline da chamada ao Identity
    /// (<c>Identity:GrpcTimeoutSeconds</c>, default 2s), para que o cliente
    /// não tente de novo antes de uma folga razoável.
    /// </summary>
    private const int RetryAfterSeconds = 5;

    private static IResult ToProblem(Error error)
    {
        var statusCode = error.Type.ToStatusCode();

        var problem = Microsoft.AspNetCore.Http.Results.Problem(
            statusCode: statusCode,
            type: $"https://httpstatuses.io/{statusCode}",
            detail: error.Message,
            extensions: new Dictionary<string, object?> { ["errorCode"] = error.Code });

        // D-28: todo 503 desta origem é explicitamente temporário — o
        // cliente tem um cabeçalho para basear a nova tentativa, em vez de
        // adivinhar. Nenhum outro ErrorType leva Retry-After.
        return error.Type == ErrorType.Unavailable
            ? new ResultWithRetryAfter(problem, RetryAfterSeconds)
            : problem;
    }

    /// <summary>
    /// Envolve um <see cref="IResult"/> já pronto só para acrescentar o
    /// cabeçalho <c>Retry-After</c> antes de executá-lo — <c>Results.Problem</c>
    /// não tem parâmetro para cabeçalhos arbitrários.
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
            httpContext.Response.Headers.RetryAfter = _retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

            return _inner.ExecuteAsync(httpContext);
        }
    }
}

/// <summary>
/// Mapeamento <see cref="ErrorType"/> → status HTTP (BE-03, CA-02). Cobre
/// também <see cref="ErrorType.Unavailable"/> (503), acrescentado além da
/// tabela original de BE-03 — é o código que BE-28 usa para
/// <c>identity.unavailable</c>.
/// </summary>
public static class ErrorTypeHttpMapping
{
    public static int ToStatusCode(this ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
        ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
        ErrorType.Failure => StatusCodes.Status500InternalServerError,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "ErrorType sem mapeamento HTTP definido."),
    };
}
