using Microsoft.EntityFrameworkCore;
using TodoList.Identity.Application.Sessions;
using TodoList.Identity.Domain.Sessions;
using TodoList.Identity.Infrastructure.Persistence;

namespace TodoList.Identity.Infrastructure.Sessions;

/// <summary>
/// <see cref="IRefreshTokenRepository"/> sobre o <see cref="IdentityDbContext"/> (BE-10),
/// <c>Scoped</c> como o <c>DbContext</c>. <see cref="TryConsumeAsync"/> e
/// <see cref="RevokeSessionAsync"/> usam <c>ExecuteUpdateAsync</c>: um único
/// UPDATE condicional no banco, nunca ler-verificar-escrever — é o que garante
/// que dois redeems concorrentes do mesmo token não tenham sucesso os dois.
/// </summary>
public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IdentityDbContext _context;

    public RefreshTokenRepository(IdentityDbContext context)
    {
        _context = context;
    }

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        _context.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    public void Add(RefreshToken token) => _context.RefreshTokens.Add(token);

    public async Task<bool> TryConsumeAsync(Guid tokenId, Guid replacedByTokenId, DateTime now, CancellationToken cancellationToken)
    {
        var affected = await _context.RefreshTokens
            .Where(token => token.Id == tokenId
                && token.ConsumedAt == null
                && token.RevokedAt == null
                && token.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.ConsumedAt, now)
                    .SetProperty(token => token.ReplacedByTokenId, replacedByTokenId),
                cancellationToken);

        return affected == 1;
    }

    public Task RevokeSessionAsync(Guid sessionId, RefreshTokenRevocationReason reason, DateTime now, CancellationToken cancellationToken) =>
        _context.RefreshTokens
            .Where(token => token.SessionId == sessionId && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.RevokedAt, now)
                    .SetProperty(token => token.RevokedReason, reason),
                cancellationToken);

    // ponytail: carrega as linhas para o tracker (atomicidade com a troca de senha). Cresce com as
    // rotações até o expurgo de BE-23; se pesar, filtrar ConsumedAt == null aqui.
    public async Task RevokeAllForUserAsync(Guid userId, RefreshTokenRevocationReason reason, DateTime now, CancellationToken cancellationToken)
    {
        var tokens = await _context.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.Revoke(reason, now);
        }
    }
}
