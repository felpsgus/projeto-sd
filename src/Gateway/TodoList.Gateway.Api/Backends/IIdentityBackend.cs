namespace TodoList.Gateway.Api.Backends;

/// <summary>
/// Fronteira entre o Gateway e o Identity Service (BE-36) — a única
/// abstração que <see cref="Endpoints.AuthEndpoints"/> conhece; nenhum código
/// do Gateway vê o cliente gRPC gerado diretamente. Desde BE-40/D-38, a
/// autenticação de toda requisição de entrada é feita localmente (
/// <c>AddJwtBearer</c>, ver <see cref="Authentication.ServiceCollectionExtensions"/>)
/// — <c>ValidateToken</c> deixou de ter consumidor no Gateway, e por isso
/// não aparece mais aqui; o único RPC restante é <c>Login</c>.
/// </summary>
public interface IIdentityBackend
{
    /// <summary>Chama <c>Login</c> (BE-33, RN-AUTH-09) — nunca lança para "credencial inválida", só para indisponibilidade.</summary>
    public Task<LoginOutcome> LoginAsync(string email, string password, CancellationToken cancellationToken);
}

/// <summary>Resultado de <see cref="IIdentityBackend.LoginAsync"/> — <see cref="Succeeded"/>=false cobre todas as causas de credencial inválida (RN-AUTH-09).</summary>
public sealed record LoginOutcome(bool Succeeded, string AccessToken, DateTimeOffset ExpiresAt);
