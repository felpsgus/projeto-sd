using Microsoft.AspNetCore.Mvc;
using TodoList.Gateway.Api.Backends;
using TodoList.Gateway.Api.Contracts;
using TodoList.Gateway.Api.Validation;

namespace TodoList.Gateway.Api.Endpoints;

/// <summary>
/// <c>POST /api/auth/login</c> (BE-36, RN-AUTH-08/RN-AUTH-09, D-36) e
/// <c>POST /api/auth/register</c> (BE-07) — os dois únicos endpoints
/// anônimos de negócio, além de <c>/health</c> e a documentação
/// OpenAPI/Scalar (CA-14). <c>Login</c> traduz para o RPC de mesmo nome do
/// Identity; <c>succeeded=false</c> vira sempre a mesma resposta 401, para
/// e-mail inexistente, senha errada ou usuário inativo (CA-03) — o Gateway
/// não sabe (nem quer saber) qual das três aconteceu. <c>Register</c> é
/// diferente: e-mail duplicado e senha fora da política são sempre status
/// gRPC (D-35, nota técnica de BE-07 no .proto) — o vazamento de existência
/// aqui é aceitável e desejável, ao contrário do login.
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

        endpoints.MapPost("/api/auth/register", HandleRegisterAsync)
            .WithRequestValidation<RegisterHttpRequest>()
            .WithName("Register")
            .WithSummary("Cria uma nova conta de usuário (BE-07); não autentica automaticamente.")
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

    /// <summary>
    /// BE-07, CA-01 — sucesso devolve 201. Sem <c>Location</c>: não existe
    /// <c>GET /api/users/{id}</c> (BE-14, ausência intencional — nenhuma
    /// superfície de enumeração de usuários), e o cadastro não autentica
    /// automaticamente, então não haveria um <c>GET /api/me</c> acessível sem
    /// login primeiro. Conflito de e-mail (409) e senha fora da política
    /// (400) chegam como <see cref="Backends.BackendCallException"/> e são
    /// traduzidos pelo <see cref="ErrorHandling.GrpcErrorMapping"/> de sempre
    /// — nenhum tratamento especial aqui.
    /// </summary>
    private static async Task<IResult> HandleRegisterAsync(
        RegisterHttpRequest request, IIdentityBackend identityBackend, CancellationToken cancellationToken)
    {
        var profile = await identityBackend.RegisterAsync(request.Email, request.Password, request.DisplayName, cancellationToken);

        return Results.Json(profile, statusCode: StatusCodes.Status201Created);
    }
}
