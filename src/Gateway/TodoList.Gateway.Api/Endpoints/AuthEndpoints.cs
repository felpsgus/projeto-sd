using Microsoft.AspNetCore.Mvc;
using TodoList.Gateway.Api.Backends;
using TodoList.Gateway.Api.Contracts;
using TodoList.Gateway.Api.Validation;

namespace TodoList.Gateway.Api.Endpoints;

/// <summary>
/// <c>POST /api/auth/login</c> (BE-36, RN-AUTH-08/RN-AUTH-09, D-36) — único
/// endpoint anônimo além de <c>/health</c> e a documentação OpenAPI/Scalar
/// (CA-14). Traduz para o RPC <c>Login</c> do Identity; <c>succeeded=false</c>
/// vira sempre a mesma resposta 401, para e-mail inexistente, senha errada ou
/// usuário inativo (CA-03) — o Gateway não sabe (nem quer saber) qual das
/// três aconteceu.
/// </summary>
public sealed class AuthEndpoints : IEndpointRouteHandler
{
    /// <summary>ErrorCode do 401 de credencial inválida (RN-AUTH-09, CA-03).</summary>
    public const string InvalidCredentialsErrorCode = "auth.invalid_credentials";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", HandleLoginAsync)
            .WithRequestValidation<LoginHttpRequest>()
            .WithName("Login")
            .WithSummary("Autentica um usuário e devolve um access token (D-36: sem refresh token/cookie).")
            .WithTags("Auth")
            .AllowAnonymous();
    }

    private static async Task<IResult> HandleLoginAsync(
        LoginHttpRequest request, IIdentityBackend identityBackend, CancellationToken cancellationToken)
    {
        // BackendUnavailableException propositalmente não é capturada aqui —
        // o GlobalExceptionHandler a converte em 503 (CA-24), nunca 401.
        var outcome = await identityBackend.LoginAsync(request.Email!, request.Password!, cancellationToken);

        if (!outcome.Succeeded)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Credenciais inválidas.",
                type: "https://httpstatuses.io/401",
                detail: "E-mail ou senha inválidos.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = InvalidCredentialsErrorCode });
        }

        return Results.Ok(new LoginHttpResponse(outcome.AccessToken, outcome.ExpiresAt));
    }
}
