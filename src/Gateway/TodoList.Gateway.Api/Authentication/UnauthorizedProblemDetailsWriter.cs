using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace TodoList.Gateway.Api.Authentication;

/// <summary>
/// Corpo 401 único do Gateway (BE-40, CA-15) — mesmo formato que a antiga
/// <c>IdentityTokenAuthenticationHandler.HandleChallengeAsync</c> escrevia
/// antes de D-38: nenhuma causa (token ausente, expirado, assinatura
/// inválida, algoritmo fora da lista permitida, issuer/audience errados) é
/// distinguível pelo cliente. Chamado pelo
/// <c>JwtBearerEvents.OnChallenge</c> registrado em
/// <see cref="ServiceCollectionExtensions"/>.
/// </summary>
public static class UnauthorizedProblemDetailsWriter
{
    /// <summary>ErrorCode do 401 (CA-15) — mesmo valor usado antes de D-38.</summary>
    public const string ErrorCode = "auth.unauthorized";

    public static async Task WriteAsync(HttpContext httpContext, IProblemDetailsService problemDetailsService)
    {
        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        httpContext.Response.ContentType = "application/problem+json";

        // CA-15: WWW-Authenticate fica só "Bearer" — sem error/error_description,
        // que o JwtBearerHandler escreveria por padrão e que revelariam a
        // causa (ausente vs. expirado vs. assinatura inválida vs. claims
        // errados). Quem chama este método já invocou context.HandleResponse(),
        // o que descarta esse header padrão — por isso ele precisa ser escrito
        // aqui, explicitamente.
        httpContext.Response.Headers.WWWAuthenticate = "Bearer";

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Não autenticado.",
            Type = "https://httpstatuses.io/401",
            Detail = "Autenticação ausente, inválida ou expirada.",
            Instance = httpContext.Request.GetEncodedPathAndQuery(),
            Extensions = { ["errorCode"] = ErrorCode },
        };

        // Passa pelo IProblemDetailsService (em vez de escrever o JSON direto)
        // para que o CustomizeProblemDetails de AddApiErrorHandling acrescente
        // o traceId — mesmo mecanismo do GlobalExceptionHandler. Sem isto, o
        // 401 era o único corpo de erro do Gateway sem traceId.
        var wrote = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });

        if (!wrote)
        {
            await httpContext.Response.WriteAsJsonAsync(problem);
        }
    }
}
