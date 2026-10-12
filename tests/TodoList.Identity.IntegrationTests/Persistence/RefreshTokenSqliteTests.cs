using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Sessions;
using TodoList.Identity.Domain.Sessions;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Identity.Infrastructure.Sessions;
using Xunit;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// <see cref="RefreshTokenRepository"/> + <see cref="RefreshTokenService"/> sobre o
/// <see cref="IdentityDbContext"/> real em SQLite in-memory — roda sem Docker e
/// prova o mapeamento, o UPDATE condicional (<c>ExecuteUpdateAsync</c>, linhas
/// afetadas) e a FK com cascata. A concorrência de verdade e as constraints do
/// Postgres estão em <see cref="RefreshTokenPostgresTests"/>.
/// </summary>
public sealed class RefreshTokenSqliteTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T10:00:00Z"));
    private readonly IdentityDbContext _context;
    private readonly RefreshTokenRepository _repository;
    private readonly RefreshTokenService _service;
    private readonly User _user;

    public RefreshTokenSqliteTests()
    {
        _connection.Open();
        _context = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options);
        _context.Database.EnsureCreated();

        _user = User.Create(Email.Create("ada@exemplo.com").Value, "Ada", "hash", _time).Value;
        _context.Users.Add(_user);
        _context.SaveChanges();

        _repository = new RefreshTokenRepository(_context);
        _service = new RefreshTokenService(_repository, _context, _time, TimeSpan.FromDays(7));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact] // CA-17, CA-20 (modelo)
    public void Modelo_TemIndiceUnicoEmTokenHashEIndiceUserSession()
    {
        var entity = _context.Model.FindEntityType(typeof(RefreshToken))!;

        entity.GetIndexes().Should().Contain(index => index.IsUnique && index.Properties.Single().Name == nameof(RefreshToken.TokenHash));
        entity.GetIndexes().Should().Contain(index => string.Join(',', index.Properties.Select(p => p.Name)) == "UserId,SessionId");
    }

    [Fact] // CA-17
    public async Task Issue_PersisteSoOHash()
    {
        var issued = await _service.IssueAsync(_user.Id, null, CancellationToken.None);

        var stored = await _context.RefreshTokens.AsNoTracking().SingleAsync();
        stored.TokenHash.Should().Be(RefreshTokenSecret.Hash(issued.Value));
        (await _context.RefreshTokens.AnyAsync(token => token.TokenHash == issued.Value)).Should().BeFalse();
    }

    [Fact] // CA-12 (semântica do UPDATE condicional): só a primeira tentativa afeta uma linha
    public async Task TryConsume_SoUmaVezPorToken()
    {
        var issued = await _service.IssueAsync(_user.Id, null, CancellationToken.None);
        var id = (await _context.RefreshTokens.AsNoTracking().SingleAsync()).Id;
        var now = _time.GetUtcNow().UtcDateTime;

        var first = await _repository.TryConsumeAsync(id, Guid.NewGuid(), now, CancellationToken.None);
        var second = await _repository.TryConsumeAsync(id, Guid.NewGuid(), now, CancellationToken.None);

        first.Should().BeTrue();
        second.Should().BeFalse();
        issued.Should().NotBeNull();
    }

    [Fact]
    public async Task TryConsume_ExpiradoOuRevogado_NaoConsome()
    {
        var expired = await _service.IssueAsync(_user.Id, null, CancellationToken.None);
        var revoked = await _service.IssueAsync(_user.Id, null, CancellationToken.None);
        await _service.RevokeSessionAsync(revoked.SessionId, RefreshTokenRevocationReason.Logout, CancellationToken.None);
        var ids = await _context.RefreshTokens.AsNoTracking().ToDictionaryAsync(token => token.SessionId, token => token.Id);

        var revokedResult = await _repository.TryConsumeAsync(ids[revoked.SessionId], Guid.NewGuid(), _time.GetUtcNow().UtcDateTime, CancellationToken.None);
        var expiredResult = await _repository.TryConsumeAsync(
            ids[expired.SessionId], Guid.NewGuid(), _time.GetUtcNow().UtcDateTime.AddDays(8), CancellationToken.None);

        revokedResult.Should().BeFalse();
        expiredResult.Should().BeFalse();
    }

    [Fact] // CA-01..CA-09 de BE-10 ponta a ponta, com o repositório real
    public async Task Fluxo_Login_Refresh_Reuso_DerrubaACadeia()
    {
        var login = await _service.IssueAsync(_user.Id, null, CancellationToken.None);

        var first = await _service.RedeemAsync(login.Value, CancellationToken.None);
        var second = await _service.RedeemAsync(first.Value.Next.Value, CancellationToken.None);
        var reuse = await _service.RedeemAsync(login.Value, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        reuse.Error.Should().Be(AuthErrors.InvalidRefreshToken);
        (await _service.RedeemAsync(second.Value.Next.Value, CancellationToken.None)).IsFailure.Should().BeTrue();

        var chain = await _context.RefreshTokens.AsNoTracking().ToListAsync();
        chain.Should().HaveCount(3).And.OnlyContain(token => token.SessionId == login.SessionId && token.RevokedAt != null);
        chain.Should().OnlyContain(token => token.RevokedReason == RefreshTokenRevocationReason.ReuseDetected);
        chain.Count(token => token.ConsumedAt != null && token.ReplacedByTokenId != null).Should().Be(2);
    }

    [Fact] // BE-11 CA-03: RevokedAt/RevokedReason no banco; só a sessão alvo
    public async Task Logout_RevogaASessaoComMotivoLogoutEPoupaAsOutras()
    {
        var a = await _service.IssueAsync(_user.Id, null, CancellationToken.None);
        var b = await _service.IssueAsync(_user.Id, null, CancellationToken.None);

        await _service.RevokeSessionOfTokenAsync(_user.Id, a.Value, CancellationToken.None);

        var tokens = await _context.RefreshTokens.AsNoTracking().ToListAsync();
        tokens.Single(token => token.SessionId == a.SessionId).RevokedReason.Should().Be(RefreshTokenRevocationReason.Logout);
        tokens.Single(token => token.SessionId == b.SessionId).RevokedAt.Should().BeNull();
    }

    [Fact] // RN-AUTH-19: revogação "por usuário" entra no SaveChanges de quem chama
    public async Task RevokeAllForUser_SoPersisteNoSaveChanges()
    {
        await _service.IssueAsync(_user.Id, null, CancellationToken.None);
        await _service.IssueAsync(_user.Id, null, CancellationToken.None);

        await _service.RevokeAllForUserAsync(_user.Id, RefreshTokenRevocationReason.PasswordChanged, CancellationToken.None);
        (await _context.RefreshTokens.AsNoTracking().CountAsync(token => token.RevokedAt == null)).Should().Be(2, "ainda não comitado");

        await _context.SaveChangesAsync();

        var tokens = await _context.RefreshTokens.AsNoTracking().ToListAsync();
        tokens.Should().OnlyContain(token => token.RevokedReason == RefreshTokenRevocationReason.PasswordChanged);
    }

    [Fact] // RN-AUTH-19 / BE-16: excluir o usuário leva os tokens (cascata da FK)
    public async Task ExcluirUsuario_RemoveOsRefreshTokensPorCascata()
    {
        await _service.IssueAsync(_user.Id, null, CancellationToken.None);
        await _service.IssueAsync(_user.Id, null, CancellationToken.None);

        _context.Users.Remove(_user);
        await _context.SaveChangesAsync();

        (await _context.RefreshTokens.CountAsync()).Should().Be(0);
    }
}
