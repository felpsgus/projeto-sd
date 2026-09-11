using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TodoList.Gateway.Api.Backends;

namespace TodoList.Gateway.Api.Authentication;

/// <summary>
/// Autenticação do Gateway (BE-36) via <c>ValidateToken</c> gRPC no Identity
/// (D-31) — nunca valida o JWT localmente, porque a chave de assinatura não
/// sai do Identity. É o mecanismo idiomático de autenticação do ASP.NET Core
/// (<see cref="AuthenticationHandler{TOptions}"/>): integra com
/// <c>RequireAuthorization</c>/<c>AllowAnonymous</c> e com
/// <see cref="HandleChallengeAsync"/> sem reinventar a resposta 401, e é
/// testável isolado via <c>TestServer</c>.
///
/// <para>
/// <b>Três desfechos de <see cref="HandleAuthenticateAsync"/>:</b>
/// (1) sem <c>Authorization: Bearer</c> → <see cref="AuthenticateResult.NoResult"/>
/// (deixa a autorização decidir se o endpoint exige autenticação);
/// (2) com Bearer, token inválido/expirado (<c>valid=false</c>) →
/// <see cref="AuthenticateResult.Fail(string)"/>; (3) válido →
/// <see cref="AuthenticateResult.Success"/> com um <see cref="ClaimsPrincipal"/>
/// cujo claim <see cref="IdentityClaimTypes.Subject"/> é o <c>user_id</c>
/// devolvido pelo Identity. Nos três casos, <see cref="HandleChallengeAsync"/>
/// escreve exatamente o mesmo corpo 401 (CA-12) — a distinção entre "ausente"
/// e "inválido" nunca chega ao cliente.
/// </para>
///
/// <para>
/// <b>Falha por indisponibilidade do Identity nunca vira 401 (CA-13).</b>
/// <see cref="IIdentityBackend.ValidateTokenAsync"/> lança
/// <see cref="BackendUnavailableException"/> quando o Identity está fora do
/// ar durante a validação — essa exceção atravessa este handler sem ser
/// capturada e é tratada, mais adiante no pipeline, pelo
/// <see cref="ErrorHandling.GlobalExceptionHandler"/> (503 + <c>Retry-After</c>):
/// um 401 nesse caminho diria "sua sessão é inválida" quando na verdade é o
/// Identity que está fora, e o cliente tentaria logar de novo à toa.
/// </para>
/// </summary>
public sealed class IdentityTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>ErrorCode do corpo 401 (CA-12) — mesmo formato de <c>ProblemDetails.extensions.errorCode</c> usado por Identity/Tasks.</summary>
    public const string UnauthorizedErrorCode = "auth.unauthorized";

    private readonly IIdentityBackend _identityBackend;

    public IdentityTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IIdentityBackend identityBackend)
        : base(options, logger, encoder)
    {
        _identityBackend = identityBackend;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(header) || !header.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        var accessToken = header[BearerPrefix.Length..].Trim();

        if (accessToken.Length == 0)
        {
            return AuthenticateResult.NoResult();
        }

        // BackendUnavailableException propositalmente não é capturada aqui —
        // ver nota técnica da classe (CA-13): precisa chegar ao
        // GlobalExceptionHandler como 503, nunca virar Fail (401).
        var validation = await _identityBackend.ValidateTokenAsync(accessToken, Context.RequestAborted);

        if (!validation.IsValid)
        {
            return AuthenticateResult.Fail("Token inválido ou expirado.");
        }

        var claims = new[] { new Claim(IdentityClaimTypes.Subject, validation.UserId) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // CA-12: mesmo corpo 401 para token ausente, inválido ou expirado —
        // nenhuma das três causas é distinguível pelo cliente.
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Não autenticado.",
            Type = "https://httpstatuses.io/401",
            Detail = "Autenticação ausente, inválida ou expirada.",
            Instance = Request.GetEncodedPathAndQuery(),
            Extensions = { ["errorCode"] = UnauthorizedErrorCode },
        };

        await Response.WriteAsJsonAsync(problem);
    }
}
