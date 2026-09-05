using Microsoft.AspNetCore.Http;
using TodoList.SharedKernel;

namespace TodoList.Identity.Api.ResultMapping;

/// <summary>
/// Extensão que converte <see cref="Result"/>/<see cref="Result{TValue}"/> em
/// <see cref="IResult"/> de Minimal API (BE-03) — endpoints não precisam
/// escrever o <c>switch</c> de <see cref="ErrorType"/> para status HTTP
/// (CA-08). Mora aqui, e não em <c>TodoList.SharedKernel</c>, porque depende
/// de <c>Microsoft.AspNetCore.Http</c> — o <c>SharedKernel</c> não pode
/// arrastar esse pacote (D-26, CA-10). Duplicada em
/// <c>TodoList.Tasks.Api</c>: são poucas linhas, e o preço de compartilhar
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

    private static IResult ToProblem(Error error)
    {
        var statusCode = error.Type.ToStatusCode();

        return Microsoft.AspNetCore.Http.Results.Problem(
            statusCode: statusCode,
            type: $"https://httpstatuses.io/{statusCode}",
            detail: error.Message,
            extensions: new Dictionary<string, object?> { ["errorCode"] = error.Code });
    }
}

/// <summary>
/// Mapeamento <see cref="ErrorType"/> → status HTTP (BE-03, CA-02). Cobre
/// também <see cref="ErrorType.Unavailable"/> (503), acrescentado além da
/// tabela original de BE-03 porque BE-28 precisa dele para
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
