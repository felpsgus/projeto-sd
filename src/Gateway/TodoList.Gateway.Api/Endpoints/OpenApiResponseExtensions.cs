namespace TodoList.Gateway.Api.Endpoints;

/// <summary>
/// Respostas documentadas no OpenAPI (BE-24, CA-22). Os handlers devolvem <c>IResult</c>, então o OpenAPI não
/// infere nada: cada endpoint declara aqui, à mão, todos os códigos que pode responder.
/// </summary>
public static class OpenApiResponseExtensions
{
    /// <summary>
    /// <c>application/problem+json</c> (RFC 9457) para cada status de erro — o 400 é o <c>ValidationProblem</c>
    /// com <c>errors</c> por campo; os demais levam <c>errorCode</c> estável (<c>traceId</c> sempre).
    /// </summary>
    public static RouteHandlerBuilder ProducesProblems(this RouteHandlerBuilder builder, params int[] statusCodes)
    {
        foreach (var statusCode in statusCodes)
        {
            _ = statusCode == StatusCodes.Status400BadRequest
                ? builder.ProducesValidationProblem()
                : builder.ProducesProblem(statusCode);
        }

        return builder;
    }
}
