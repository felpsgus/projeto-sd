using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Domain.Sessions;
using TodoList.SharedKernel;

namespace TodoList.Identity.Application.Sessions;

/// <summary>
/// Ciclo de vida dos refresh tokens (BE-10, RN-AUTH-14..20) sobre
/// <see cref="IRefreshTokenRepository"/>. A duração vem de configuração
/// (<c>Jwt:RefreshTokenDays</c>), injetada pela fiação da Infrastructure.
///
/// <para>
/// <b>Rotação sem transação explícita.</b> O token novo é inserido <i>antes</i>
/// de o antigo ser consumido. Se o consumo falha (perdeu a corrida), o chamador
/// trata como reuso e revoga a sessão inteira — inclusive o token recém-inserido
/// e o do vencedor, que já está commitado porque o consumo do vencedor só
/// acontece depois do seu insert. Resultado: nunca sobram dois tokens ativos
/// derivados do mesmo pai (CA-12), sem precisar de transação.
/// </para>
/// </summary>
public sealed class RefreshTokenService
{
    private readonly IRefreshTokenRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lifetime;

    public RefreshTokenService(IRefreshTokenRepository repository, IUnitOfWork unitOfWork, TimeProvider timeProvider, TimeSpan lifetime)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _lifetime = lifetime;
    }

    /// <summary>
    /// Emite e persiste um token novo. Sem <paramref name="sessionId"/>, abre
    /// uma sessão nova (cada login = uma sessão, D-15); com ele, continua a cadeia.
    /// </summary>
    public async Task<IssuedRefreshToken> IssueAsync(Guid userId, Guid? sessionId, CancellationToken cancellationToken)
    {
        var (token, issued) = Create(userId, sessionId ?? Guid.NewGuid());

        _repository.Add(token);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return issued;
    }

    /// <summary>
    /// Troca um token válido por outro na mesma sessão (RN-AUTH-16). Inexistente,
    /// vazio ou expirado → <see cref="AuthErrors.InvalidRefreshToken"/>; revogado por
    /// logout/troca de senha → <see cref="AuthErrors.RefreshTokenRevoked"/> (RN-AUTH-19).
    /// Já consumido (ou perdeu a corrida de um redeem concorrente) → reuso: a
    /// cadeia inteira da sessão é revogada (RN-AUTH-17) e o mesmo erro volta.
    /// Só a revogação por ação do usuário é distinguível.
    /// </summary>
    public async Task<Result<RedeemedRefreshToken>> RedeemAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Failure<RedeemedRefreshToken>(AuthErrors.InvalidRefreshToken);
        }

        var existing = await _repository.FindByHashAsync(RefreshTokenSecret.Hash(token), cancellationToken);

        if (existing is null)
        {
            return Result.Failure<RedeemedRefreshToken>(AuthErrors.InvalidRefreshToken);
        }

        if (existing.RevokedAt is not null)
        {
            // RN-AUTH-19: revogação por ação do usuário é distinguível; a por reuso (RN-AUTH-17) não.
            return Result.Failure<RedeemedRefreshToken>(
                existing.RevokedReason == RefreshTokenRevocationReason.ReuseDetected
                    ? AuthErrors.InvalidRefreshToken
                    : AuthErrors.RefreshTokenRevoked);
        }

        if (existing.ConsumedAt is not null)
        {
            // RN-AUTH-17: token já usado reapresentado — assume vazamento e derruba a cadeia.
            await RevokeSessionAsync(existing.SessionId, RefreshTokenRevocationReason.ReuseDetected, cancellationToken);

            return Result.Failure<RedeemedRefreshToken>(AuthErrors.InvalidRefreshToken);
        }

        var now = Now();

        if (existing.IsExpired(now))
        {
            return Result.Failure<RedeemedRefreshToken>(AuthErrors.InvalidRefreshToken);
        }

        var (next, issued) = Create(existing.UserId, existing.SessionId);

        _repository.Add(next);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (!await _repository.TryConsumeAsync(existing.Id, next.Id, now, cancellationToken))
        {
            await RevokeSessionAsync(existing.SessionId, RefreshTokenRevocationReason.ReuseDetected, cancellationToken);

            return Result.Failure<RedeemedRefreshToken>(AuthErrors.InvalidRefreshToken);
        }

        return Result.Success(new RedeemedRefreshToken(existing.UserId, issued));
    }

    /// <summary>Revoga, de imediato, a cadeia inteira de uma sessão.</summary>
    public Task RevokeSessionAsync(Guid sessionId, RefreshTokenRevocationReason reason, CancellationToken cancellationToken) =>
        _repository.RevokeSessionAsync(sessionId, reason, Now(), cancellationToken);

    /// <summary>
    /// Logout (BE-11, RN-AUTH-12): revoga a sessão do <paramref name="token"/> se ele
    /// pertencer a <paramref name="userId"/>, com motivo Logout. Idempotente — nunca
    /// lança por token ausente/desconhecido/já revogado. Token de <b>outro</b> usuário
    /// não revoga nada (BE-11 CA-09); o desfecho volta ao chamador para ele registrar
    /// o alerta (a Application não tem logger).
    /// </summary>
    public async Task<RevokeSessionOutcome> RevokeSessionOfTokenAsync(Guid userId, string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return RevokeSessionOutcome.NotFound;
        }

        var existing = await _repository.FindByHashAsync(RefreshTokenSecret.Hash(token), cancellationToken);

        if (existing is null)
        {
            return RevokeSessionOutcome.NotFound;
        }

        if (existing.UserId != userId)
        {
            return RevokeSessionOutcome.OwnedByAnotherUser;
        }

        await RevokeSessionAsync(existing.SessionId, RefreshTokenRevocationReason.Logout, cancellationToken);

        return RevokeSessionOutcome.Revoked;
    }

    /// <summary>
    /// Marca todos os tokens do usuário como revogados <b>sem salvar</b> — quem
    /// chama comita com <c>IUnitOfWork.SaveChangesAsync</c>, no mesmo commit da
    /// mudança que motivou a revogação (troca de senha, logout-all).
    /// </summary>
    public Task RevokeAllForUserAsync(Guid userId, RefreshTokenRevocationReason reason, CancellationToken cancellationToken) =>
        _repository.RevokeAllForUserAsync(userId, reason, Now(), cancellationToken);

    private DateTime Now() => _timeProvider.GetUtcNow().UtcDateTime;

    private (RefreshToken Token, IssuedRefreshToken Issued) Create(Guid userId, Guid sessionId)
    {
        var value = RefreshTokenSecret.Generate();
        var token = RefreshToken.Issue(userId, sessionId, RefreshTokenSecret.Hash(value), Now(), _lifetime);

        return (token, new IssuedRefreshToken(value, new DateTimeOffset(token.ExpiresAt, TimeSpan.Zero), sessionId));
    }
}
