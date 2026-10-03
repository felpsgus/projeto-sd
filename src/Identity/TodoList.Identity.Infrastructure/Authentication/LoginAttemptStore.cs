using Microsoft.EntityFrameworkCore;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Infrastructure.Persistence;

namespace TodoList.Identity.Infrastructure.Authentication;

/// <summary>
/// <see cref="ILoginAttemptStore"/> sobre Postgres (BE-12). <see cref="RegisterAttemptAsync"/>
/// é um único <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c>: o banco serializa as
/// tentativas concorrentes da mesma chave (lock de linha), então cada uma enxerga um
/// contador distinto — nunca ler-verificar-escrever (CA-10). O relógio vem do chamador
/// (<c>TimeProvider</c>), não de <c>now()</c>, para os testes avançarem o tempo.
/// </summary>
public sealed class LoginAttemptStore : ILoginAttemptStore
{
    private readonly IdentityDbContext _context;

    public LoginAttemptStore(IdentityDbContext context)
    {
        _context = context;
    }

    public async Task<LoginAttemptState> RegisterAttemptAsync(
        string email, DateTime now, int maxAttempts, TimeSpan lockout, TimeSpan window, CancellationToken cancellationToken)
    {
        var lockedUntil = now + lockout;
        var windowStart = now - window;

        // "Livre" = sem bloqueio vigente. Reinício: bloqueio expirado ou janela vencida (CA-04, CA-09).
        // Bloqueado: contador sobe (marca "recusada"), LockedUntil e LastAttemptAt ficam como estão.
        var row = await _context.Database
            .SqlQuery<AttemptRow>($"""
                INSERT INTO identity.login_attempts AS a (normalized_email, failed_count, last_attempt_at, locked_until)
                VALUES ({email}, 1, {now}, CASE WHEN 1 >= {maxAttempts} THEN {lockedUntil} END)
                ON CONFLICT (normalized_email) DO UPDATE SET
                    failed_count = CASE
                        WHEN a.locked_until > {now} THEN a.failed_count + 1
                        WHEN a.locked_until <= {now} OR a.last_attempt_at <= {windowStart} THEN 1
                        ELSE a.failed_count + 1 END,
                    last_attempt_at = CASE WHEN a.locked_until > {now} THEN a.last_attempt_at ELSE {now} END,
                    locked_until = CASE
                        WHEN a.locked_until > {now} THEN a.locked_until
                        WHEN a.locked_until <= {now} OR a.last_attempt_at <= {windowStart}
                            THEN CASE WHEN 1 >= {maxAttempts} THEN {lockedUntil} END
                        WHEN a.failed_count + 1 >= {maxAttempts} THEN {lockedUntil}
                        END
                RETURNING failed_count, locked_until
                """)
            .ToListAsync(cancellationToken);

        return new LoginAttemptState(row[0].FailedCount, row[0].LockedUntil);
    }

    public Task ResetAsync(string email, CancellationToken cancellationToken) =>
        _context.LoginAttempts.Where(attempt => attempt.NormalizedEmail == email).ExecuteDeleteAsync(cancellationToken);

    private sealed record AttemptRow(int FailedCount, DateTime? LockedUntil);
}
