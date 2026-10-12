using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.SharedKernel.Persistence;
using TodoList.SharedKernel.Retention;

namespace TodoList.Identity.Infrastructure.Retention;

/// <summary>
/// Expurgo do schema <c>identity</c> (BE-23): refresh tokens vencidos/revogados além de
/// <c>Auth:TokenRetentionDays</c> e linhas de <c>login_attempts</c> sem relevância.
/// <c>refresh_tokens.replaced_by_token_id</c> não tem FK, então a ordem de exclusão é livre.
/// </summary>
public sealed class IdentityRetentionPurger : IRetentionPurger
{
    private readonly IdentityDbContext _context;
    private readonly TimeProvider _time;
    private readonly int _tokenRetentionDays;
    private readonly TimeSpan _attemptWindow;
    private readonly int _batchSize;

    public IdentityRetentionPurger(
        IdentityDbContext context,
        TimeProvider time,
        IOptions<AuthOptions> auth,
        IOptions<LockoutOptions> lockout,
        IOptions<RetentionOptions> retention)
    {
        _context = context;
        _time = time;
        _tokenRetentionDays = auth.Value.TokenRetentionDays;
        _attemptWindow = TimeSpan.FromMinutes(lockout.Value.AttemptWindowMinutes);
        _batchSize = retention.Value.BatchSize;
    }

    public async Task<IReadOnlyDictionary<string, int>> PurgeAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var tokenCutoff = now.AddDays(-_tokenRetentionDays);
        var attemptCutoff = now - _attemptWindow;

        // Token ainda válido tem ExpiresAt no futuro e RevokedAt nulo: nunca casa (CA-05).
        var tokens = await BatchDelete.RunAsync(
            ct =>
            {
                var batch = _context.RefreshTokens
                    .Where(token => token.ExpiresAt < tokenCutoff || token.RevokedAt < tokenCutoff)
                    .OrderBy(token => token.CreatedAt)
                    .Take(_batchSize)
                    .Select(token => token.Id);

                return _context.RefreshTokens.Where(token => batch.Contains(token.Id)).ExecuteDeleteAsync(ct);
            },
            _batchSize,
            cancellationToken);

        // Fora da janela o contador reiniciaria de qualquer forma; bloqueio vigente nunca sai (CA-06).
        var attempts = await BatchDelete.RunAsync(
            ct =>
            {
                var batch = _context.LoginAttempts
                    .Where(attempt => attempt.LastAttemptAt < attemptCutoff
                        && (attempt.LockedUntil == null || attempt.LockedUntil <= now))
                    .OrderBy(attempt => attempt.LastAttemptAt)
                    .Take(_batchSize)
                    .Select(attempt => attempt.NormalizedEmail);

                return _context.LoginAttempts.Where(attempt => batch.Contains(attempt.NormalizedEmail)).ExecuteDeleteAsync(ct);
            },
            _batchSize,
            cancellationToken);

        return new Dictionary<string, int> { ["refresh_tokens"] = tokens, ["login_attempts"] = attempts };
    }
}
