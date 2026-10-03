using TodoList.Gateway.Api.Contracts;

namespace TodoList.Gateway.Api.Backends;

/// <summary>
/// Fronteira entre o Gateway e o Identity Service (BE-36) — a única
/// abstração que <see cref="Endpoints.AuthEndpoints"/>/<see cref="Endpoints.UserEndpoints"/>
/// conhecem; nenhum código do Gateway vê o cliente gRPC gerado diretamente.
/// O Gateway valida o JWT de toda requisição de entrada localmente
/// (<c>AddJwtBearer</c>, ver <see cref="Authentication.ServiceCollectionExtensions"/>),
/// sem chamar o Identity a cada requisição.
///
/// <para>
/// Onda 2 da Fase 3 (BE-07/BE-14/BE-15/BE-16) acrescenta os RPCs de
/// cadastro/perfil/senha/conta. Ao contrário de <c>ITasksBackend</c>, nenhum
/// deles depende de <see cref="Backends.ClientMetadataInterceptor"/> para
/// saber o usuário — o contrato <c>identity.proto</c> carrega
/// <c>user_id</c> explicitamente em cada mensagem (BE-25), então quem chama
/// aqui (<see cref="Endpoints.UserEndpoints"/>) já resolve o <c>sub</c> do
/// token (<see cref="Authentication.ClaimsPrincipalExtensions.GetUserId"/>)
/// e passa como parâmetro — nunca um valor do corpo ou da query.
/// </para>
/// </summary>
public interface IIdentityBackend
{
    /// <summary>Chama <c>Login</c> (BE-33, RN-AUTH-09) — nunca lança para "credencial inválida", só para indisponibilidade.</summary>
    public Task<LoginOutcome> LoginAsync(string email, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Chama <c>RefreshSession</c> (BE-10). Qualquer token inválido (inexistente,
    /// expirado, revogado, reuso) volta como <see cref="RefreshOutcome.Succeeded"/>=false
    /// — nunca uma exceção; só indisponibilidade lança.
    /// </summary>
    public Task<RefreshOutcome> RefreshSessionAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Chama <c>Logout</c> (BE-11) com o <c>user_id</c> do token e o refresh token do cookie. Idempotente.</summary>
    public Task LogoutAsync(string userId, string refreshToken, CancellationToken cancellationToken);

    /// <summary>Chama <c>LogoutAll</c> (BE-11, RN-AUTH-19) com o <c>user_id</c> do token.</summary>
    public Task LogoutAllAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Chama <c>Register</c> (BE-07). E-mail duplicado e senha fora da
    /// política viram <see cref="BackendCallException"/> (D-35) — ao
    /// contrário de <see cref="LoginAsync"/>, aqui a falha é sempre um status
    /// gRPC, nunca um "succeeded=false" (o vazamento de existência é
    /// desejável, nota técnica de BE-07).
    /// </summary>
    public Task<ProfileHttpResponse> RegisterAsync(string? email, string? password, string? displayName, CancellationToken cancellationToken);

    /// <summary>Chama <c>GetProfile</c> (BE-14) com o <c>user_id</c> resolvido pelo chamador a partir do <c>sub</c> do token.</summary>
    public Task<ProfileHttpResponse> GetProfileAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Chama <c>UpdateProfile</c> (BE-14, RN-USER-02) com o <c>user_id</c> resolvido pelo chamador.</summary>
    public Task<ProfileHttpResponse> UpdateProfileAsync(string userId, string? displayName, CancellationToken cancellationToken);

    /// <summary>
    /// Chama <c>ChangePassword</c> (BE-15). Senha atual incorreta vira 400
    /// (decisão do tech lead — ver <c>AuthErrors.InvalidCurrentPassword</c>
    /// no Identity), propagado pelo mesmo <see cref="BackendCallException"/>/
    /// <see cref="ErrorHandling.GrpcErrorMapping"/> de qualquer outra falha
    /// de validação — nenhum tratamento especial aqui.
    /// </summary>
    public Task ChangePasswordAsync(string userId, string? currentPassword, string? newPassword, CancellationToken cancellationToken);

    /// <summary>Chama <c>DeleteAccount</c> (BE-16, D-19) com o <c>user_id</c> resolvido pelo chamador.</summary>
    public Task DeleteAccountAsync(string userId, string? password, CancellationToken cancellationToken);
}

/// <summary>Resultado de <see cref="IIdentityBackend.LoginAsync"/> — <see cref="Succeeded"/>=false cobre todas as causas de credencial inválida (RN-AUTH-09).</summary>
/// <remarks>
/// <see cref="RefreshToken"/> é credencial de longa duração (RN-AUTH-20): vai só para o cookie
/// HttpOnly, nunca para o corpo nem para log — por isso o <c>ToString</c> do record omite os tokens.
/// </remarks>
public sealed record LoginOutcome(
    bool Succeeded, string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken = "", DateTimeOffset RefreshTokenExpiresAt = default, int? RetryAfterSeconds = null)
{
    public override string ToString() => $"LoginOutcome {{ Succeeded = {Succeeded} }}";
}

/// <summary>Resultado de <see cref="IIdentityBackend.RefreshSessionAsync"/> — <see cref="Succeeded"/>=false cobre todas as causas de token inválido.</summary>
public sealed record RefreshOutcome(
    bool Succeeded, string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt)
{
    public override string ToString() => $"RefreshOutcome {{ Succeeded = {Succeeded} }}";
}
