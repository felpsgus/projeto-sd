using System.Security.Claims;
using TodoList.Gateway.Api.Authentication;
using TodoList.Gateway.Api.Backends;
using TodoList.Gateway.Api.Contracts;
using TodoList.Gateway.Api.Http;
using TodoList.Gateway.Api.Validation;

namespace TodoList.Gateway.Api.Endpoints;

/// <summary>
/// Endpoints de autenticação e sessão (BE-09/BE-10/BE-11, BE-36, D-20, D-36):
/// <list type="bullet">
/// <item><c>POST /api/auth/login</c> (anônimo) — traduz para o RPC <c>Login</c>;
/// <c>succeeded=false</c> vira sempre o mesmo 401 (RN-AUTH-09), exceto
/// <c>locked_out=true</c> (BE-12): 429 + <c>Retry-After</c> + <c>auth.too_many_attempts</c>. Sucesso: o
/// access token no corpo e o refresh token <b>só</b> no cookie <c>HttpOnly</c>
/// (<see cref="RefreshCookie"/>); falha nunca emite cookie.</item>
/// <item><c>POST /api/auth/refresh</c> (anônimo, corpo vazio) — o token vem
/// <b>só</b> do cookie. Toda falha, inclusive cookie ausente, é o mesmo 401
/// <c>auth.invalid_refresh_token</c> e apaga o cookie.</item>
/// <item><c>POST /api/auth/logout</c> e <c>/logout-all</c> (autenticados, corpo
/// vazio) — 204, idempotentes, apagam o cookie. O <c>user_id</c> vem do <c>sub</c>
/// do token, nunca do corpo.</item>
/// <item><c>POST /api/auth/register</c> (anônimo, BE-07) — e-mail duplicado e
/// senha fora da política são sempre status gRPC (D-35); o vazamento de
/// existência aqui é desejável, ao contrário do login.</item>
/// </list>
/// Todas as respostas de login/refresh/logout levam <c>Cache-Control: no-store</c>.
/// </summary>
public sealed class AuthEndpoints : IEndpointRouteHandler
{
    /// <summary>ErrorCode do 401 de credencial inválida (RN-AUTH-09, CA-03).</summary>
    public const string InvalidCredentialsErrorCode = "auth.invalid_credentials";

    /// <summary>ErrorCode do 429 de bloqueio por tentativas de login (BE-12, RN-AUTH-13).</summary>
    public const string TooManyAttemptsErrorCode = "auth.too_many_attempts";

    /// <summary>ErrorCode do 401 de refresh (BE-10 CA-16): o mesmo para qualquer causa.</summary>
    public const string InvalidRefreshTokenErrorCode = "auth.invalid_refresh_token";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", HandleLoginAsync)
            .WithRequestValidation<LoginHttpRequest>()
            .WithNoStore()
            .WithName("Login")
            .WithSummary("Autentica um usuário: access token no corpo, refresh token em cookie HttpOnly (D-20).")
            .WithTags("Auth")
            .AllowAnonymous();

        endpoints.MapPost("/api/auth/refresh", HandleRefreshAsync)
            .WithNoStore()
            .WithName("Refresh")
            .WithSummary("Renova a sessão com o refresh token do cookie (corpo vazio); rotaciona o cookie.")
            .WithTags("Auth")
            .AllowAnonymous();

        endpoints.MapPost("/api/auth/logout", HandleLogoutAsync)
            .WithNoStore()
            .WithName("Logout")
            .WithSummary("Encerra a sessão do cookie e apaga o cookie. Idempotente (204).")
            .WithTags("Auth");

        endpoints.MapPost("/api/auth/logout-all", HandleLogoutAllAsync)
            .WithNoStore()
            .WithName("LogoutAll")
            .WithSummary("Encerra todas as sessões do usuário e apaga o cookie (204).")
            .WithTags("Auth");

        endpoints.MapPost("/api/auth/register", HandleRegisterAsync)
            .WithRequestValidation<RegisterHttpRequest>()
            .WithName("Register")
            .WithSummary("Cria uma nova conta de usuário (BE-07); não autentica automaticamente.")
            .WithTags("Auth")
            .AllowAnonymous();
    }

    private static async Task<IResult> HandleLoginAsync(
        LoginHttpRequest request,
        HttpContext httpContext,
        IIdentityBackend identityBackend,
        RefreshCookie refreshCookie,
        CancellationToken cancellationToken)
    {
        // BackendUnavailableException propositalmente não é capturada aqui —
        // o GlobalExceptionHandler a converte em 503 (CA-24), nunca 401.
        var outcome = await identityBackend.LoginAsync(request.Email!, request.Password!, cancellationToken);

        // BE-12: bloqueio por tentativas — 429 com Retry-After, sem cookie.
        if (outcome.RetryAfterSeconds is { } retryAfterSeconds)
        {
            httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

            return AuthProblem(
                StatusCodes.Status429TooManyRequests,
                "Muitas tentativas de login.",
                "Muitas tentativas de login. Tente novamente mais tarde.",
                TooManyAttemptsErrorCode);
        }

        if (!outcome.Succeeded)
        {
            return AuthProblem(
                StatusCodes.Status401Unauthorized,
                "Credenciais inválidas.",
                "E-mail ou senha inválidos.",
                InvalidCredentialsErrorCode);
        }

        refreshCookie.Write(httpContext.Response, outcome.RefreshToken, outcome.RefreshTokenExpiresAt);

        return Results.Ok(new LoginHttpResponse(outcome.AccessToken, outcome.ExpiresAt));
    }

    private static IResult AuthProblem(int status, string title, string detail, string errorCode) =>
        Results.Problem(
            statusCode: status,
            title: title,
            type: $"https://httpstatuses.io/{status}",
            detail: detail,
            extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });

    private static async Task<IResult> HandleRefreshAsync(
        HttpContext httpContext,
        IIdentityBackend identityBackend,
        RefreshCookie refreshCookie,
        CancellationToken cancellationToken)
    {
        var refreshToken = refreshCookie.Read(httpContext.Request);

        // Sem cookie nem chega a chamar o Identity — mas a resposta é a mesma
        // de qualquer outra falha (CA-16).
        var outcome = refreshToken is null
            ? null
            : await identityBackend.RefreshSessionAsync(refreshToken, cancellationToken);

        if (outcome is not { Succeeded: true })
        {
            // CA-18c: todo 401 apaga o cookie, para o navegador não reenviar um token morto.
            refreshCookie.Delete(httpContext.Response);

            return AuthProblem(
                StatusCodes.Status401Unauthorized,
                "Sessão inválida ou expirada.",
                "Faça login novamente.",
                InvalidRefreshTokenErrorCode);
        }

        refreshCookie.Write(httpContext.Response, outcome.RefreshToken, outcome.RefreshTokenExpiresAt);

        return Results.Ok(new LoginHttpResponse(outcome.AccessToken, outcome.ExpiresAt));
    }

    private static async Task<IResult> HandleLogoutAsync(
        ClaimsPrincipal user,
        HttpContext httpContext,
        IIdentityBackend identityBackend,
        RefreshCookie refreshCookie,
        CancellationToken cancellationToken)
    {
        var refreshToken = refreshCookie.Read(httpContext.Request);

        // CA-08: sem cookie não há sessão a revogar — idempotente, 204.
        if (refreshToken is not null)
        {
            await identityBackend.LogoutAsync(user.GetUserId(), refreshToken, cancellationToken);
        }

        refreshCookie.Delete(httpContext.Response);

        return Results.NoContent();
    }

    private static async Task<IResult> HandleLogoutAllAsync(
        ClaimsPrincipal user,
        HttpContext httpContext,
        IIdentityBackend identityBackend,
        RefreshCookie refreshCookie,
        CancellationToken cancellationToken)
    {
        await identityBackend.LogoutAllAsync(user.GetUserId(), cancellationToken);

        refreshCookie.Delete(httpContext.Response);

        return Results.NoContent();
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
