using TodoList.Gateway.Api.Contracts;

namespace TodoList.Gateway.Api.Backends;

/// <summary>
/// Fronteira entre o Gateway e o Identity Service (BE-36) — a única
/// abstração que <see cref="Endpoints.AuthEndpoints"/>/<see cref="Endpoints.UserEndpoints"/>
/// conhecem; nenhum código do Gateway vê o cliente gRPC gerado diretamente.
/// Desde BE-40/D-38, a autenticação de toda requisição de entrada é feita
/// localmente (<c>AddJwtBearer</c>, ver
/// <see cref="Authentication.ServiceCollectionExtensions"/>) — <c>ValidateToken</c>
/// deixou de ter consumidor no Gateway, e por isso não aparece mais aqui.
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
public sealed record LoginOutcome(bool Succeeded, string AccessToken, DateTimeOffset ExpiresAt);
