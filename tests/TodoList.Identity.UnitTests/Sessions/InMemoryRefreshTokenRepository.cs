using NSubstitute;
using TodoList.Identity.Application.Sessions;
using TodoList.Identity.Domain.Sessions;
using TodoList.SharedKernel;

namespace TodoList.Identity.UnitTests.Sessions;

/// <summary>
/// <see cref="IRefreshTokenRepository"/> em memória para os testes de unidade
/// do ciclo de vida dos tokens. <see cref="TryConsumeAsync"/> é atômico por
/// <c>lock</c>, como o UPDATE condicional do repositório real; a atomicidade
/// <i>no Postgres</i> é provada em <c>RefreshTokenPostgresTests</c>.
/// </summary>
internal sealed class InMemoryRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly object _gate = new();
    private readonly List<RefreshToken> _tokens = [];

    /// <summary>Executado logo antes do consumo — simula outro chamador chegando primeiro (corrida).</summary>
    public Action? BeforeConsume { get; set; }

    public IReadOnlyList<RefreshToken> Tokens
    {
        get
        {
            lock (_gate)
            {
                return [.. _tokens];
            }
        }
    }

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_tokens.SingleOrDefault(token => token.TokenHash == tokenHash));
        }
    }

    public void Add(RefreshToken token)
    {
        lock (_gate)
        {
            _tokens.Add(token);
        }
    }

    public Task<bool> TryConsumeAsync(Guid tokenId, Guid replacedByTokenId, DateTime now, CancellationToken cancellationToken)
    {
        BeforeConsume?.Invoke();

        lock (_gate)
        {
            return Task.FromResult(_tokens.Single(token => token.Id == tokenId).Consume(replacedByTokenId, now));
        }
    }

    public Task RevokeSessionAsync(Guid sessionId, RefreshTokenRevocationReason reason, DateTime now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            foreach (var token in _tokens.Where(token => token.SessionId == sessionId))
            {
                token.Revoke(reason, now);
            }
        }

        return Task.CompletedTask;
    }

    public Task RevokeAllForUserAsync(Guid userId, RefreshTokenRevocationReason reason, DateTime now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            foreach (var token in _tokens.Where(token => token.UserId == userId))
            {
                token.Revoke(reason, now);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Serviço real (<see cref="RefreshTokenService"/>) sobre um repositório novo, com <c>IUnitOfWork</c> substituído.</summary>
    public static (RefreshTokenService Service, InMemoryRefreshTokenRepository Repository) CreateService(TimeProvider timeProvider, int days = 7)
    {
        var repository = new InMemoryRefreshTokenRepository();

        return (new RefreshTokenService(repository, Substitute.For<IUnitOfWork>(), timeProvider, TimeSpan.FromDays(days)), repository);
    }
}
