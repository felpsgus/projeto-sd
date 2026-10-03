using TodoList.Identity.Application.Authentication;

namespace TodoList.Identity.UnitTests.Authentication;

/// <summary>
/// <see cref="ILoginAttemptStore"/> em memória, com as mesmas regras do upsert SQL de
/// <c>LoginAttemptStore</c> (atômico por <c>lock</c>). A equivalência com o Postgres real é
/// provada em <c>LoginLockoutPostgresTests</c>, que roda os mesmos cenários contra o banco.
/// </summary>
internal sealed class InMemoryLoginAttemptStore : ILoginAttemptStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (int Count, DateTime LastAttemptAt, DateTime? LockedUntil)> _rows = [];

    public int RowCount
    {
        get
        {
            lock (_gate)
            {
                return _rows.Count;
            }
        }
    }

    public Task<LoginAttemptState> RegisterAttemptAsync(
        string email, DateTime now, int maxAttempts, TimeSpan lockout, TimeSpan window, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var lockedUntil = now + lockout;

            if (!_rows.TryGetValue(email, out var row))
            {
                row = (1, now, 1 >= maxAttempts ? lockedUntil : null);
            }
            else if (row.LockedUntil > now)
            {
                row = (row.Count + 1, row.LastAttemptAt, row.LockedUntil);
            }
            else if (row.LockedUntil <= now || row.LastAttemptAt <= now - window)
            {
                row = (1, now, 1 >= maxAttempts ? lockedUntil : null);
            }
            else
            {
                var count = row.Count + 1;
                row = (count, now, count >= maxAttempts ? lockedUntil : null);
            }

            _rows[email] = row;

            return Task.FromResult(new LoginAttemptState(row.Count, row.LockedUntil));
        }
    }

    public Task ResetAsync(string email, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _rows.Remove(email);
        }

        return Task.CompletedTask;
    }
}
