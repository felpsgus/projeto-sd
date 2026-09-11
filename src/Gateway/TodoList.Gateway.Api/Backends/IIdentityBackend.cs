namespace TodoList.Gateway.Api.Backends;

/// <summary>
/// Fronteira entre o Gateway e o Identity Service (BE-36) — a única
/// abstração que <see cref="Authentication.IdentityTokenAuthenticationHandler"/>
/// e <see cref="Endpoints.AuthEndpoints"/> conhecem; nenhum dos dois vê o
/// cliente gRPC gerado diretamente.
/// </summary>
public interface IIdentityBackend
{
    /// <summary>Chama <c>ValidateToken</c> (BE-34, D-31) — nunca lança para "token inválido", só para indisponibilidade.</summary>
    public Task<TokenValidation> ValidateTokenAsync(string accessToken, CancellationToken cancellationToken);

    /// <summary>Chama <c>Login</c> (BE-33, RN-AUTH-09) — nunca lança para "credencial inválida", só para indisponibilidade.</summary>
    public Task<LoginOutcome> LoginAsync(string email, string password, CancellationToken cancellationToken);
}

/// <summary>Resultado de <see cref="IIdentityBackend.ValidateTokenAsync"/> — nunca uma exceção para token inválido/expirado (CA-12).</summary>
public sealed record TokenValidation(bool IsValid, string UserId);

/// <summary>Resultado de <see cref="IIdentityBackend.LoginAsync"/> — <see cref="Succeeded"/>=false cobre todas as causas de credencial inválida (RN-AUTH-09).</summary>
public sealed record LoginOutcome(bool Succeeded, string AccessToken, DateTimeOffset ExpiresAt);
