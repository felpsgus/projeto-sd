using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Domain.Sessions;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Authentication;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Identity.Infrastructure.Retention;
using TodoList.Identity.Infrastructure.Security;
using TodoList.SharedKernel.Retention;
using Xunit;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// BE-23 contra Postgres real (<b>requer Docker</b>), com o purger chamado direto e
/// <see cref="FakeTimeProvider"/>: tokens e tentativas são criados já "no passado" pelos timestamps.
/// </summary>
[Collection("Postgres")]
public class IdentityRetentionPurgerTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
    private Guid _userId;

    public IdentityRetentionPurgerTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public async Task InitializeAsync()
    {
        await _fixture.EnsureStartedAsync();

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();

        var hasher = new Pbkdf2PasswordHasher(Options.Create(new PasswordHashingOptions { Iterations = 200 }));
        var user = User.Create(Domain.Users.Email.Create("ada@retention.example").Value, "Ada", hasher.Hash("senha-de-teste-123"), TimeProvider.System).Value;
        context.Users.Add(user);
        await context.SaveChangesAsync();
        _userId = user.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact] // CA-05
    [Trait("Category", "Docker")]
    public async Task Purge_RemoveTokensVencidosERevogadosAlemDaRetencao_NuncaOsValidos()
    {
        var lifetime = TimeSpan.FromDays(30);
        var expiradoHa31 = Token(Now.AddDays(-61), lifetime);
        var expiradoHa29 = Token(Now.AddDays(-59), lifetime);
        var valido = Token(Now, lifetime);
        var revogadoHa31 = Token(Now.AddDays(-40), TimeSpan.FromDays(90));
        revogadoHa31.Revoke(RefreshTokenRevocationReason.Logout, Now.AddDays(-31));
        var revogadoHa29 = Token(Now.AddDays(-40), TimeSpan.FromDays(90));
        revogadoHa29.Revoke(RefreshTokenRevocationReason.Logout, Now.AddDays(-29));
        await SaveAsync(expiradoHa31, expiradoHa29, valido, revogadoHa31, revogadoHa29);

        var result = await PurgeAsync();

        result["refresh_tokens"].Should().Be(2);
        (await TokenIdsAsync()).Should().BeEquivalentTo([expiradoHa29.Id, valido.Id, revogadoHa29.Id]);
    }

    [Fact] // CA-06
    [Trait("Category", "Docker")]
    public async Task Purge_RemoveTentativasAntigas_MasNuncaUmBloqueioAtivo()
    {
        await using (var context = CreateContext())
        {
            context.LoginAttempts.AddRange(
                Attempt("antiga@x", lastAttempt: Now.AddHours(-1), lockedUntil: null),
                Attempt("bloqueio-expirado@x", lastAttempt: Now.AddHours(-1), lockedUntil: Now.AddMinutes(-30)),
                Attempt("recente@x", lastAttempt: Now.AddMinutes(-5), lockedUntil: null),
                Attempt("bloqueada@x", lastAttempt: Now.AddHours(-1), lockedUntil: Now.AddMinutes(10)));
            await context.SaveChangesAsync();
        }

        var result = await PurgeAsync();

        result["login_attempts"].Should().Be(2);
        await using var check = CreateContext();
        (await check.LoginAttempts.Select(a => a.NormalizedEmail).ToListAsync())
            .Should().BeEquivalentTo(["recente@x", "bloqueada@x"]);
    }

    [Fact] // CA-07, CA-12
    [Trait("Category", "Docker")]
    public async Task Purge_ComMaisRegistrosQueOLote_EsvaziaOBacklogEASegundaExecucaoNaoRemoveNada()
    {
        var tokens = Enumerable.Range(0, 25).Select(_ => Token(Now.AddDays(-90), TimeSpan.FromDays(30))).ToArray();
        await SaveAsync(tokens);

        var first = await PurgeAsync(batchSize: 10);
        var second = await PurgeAsync(batchSize: 10);

        first["refresh_tokens"].Should().Be(25);
        second.Values.Should().AllBeEquivalentTo(0);
        (await TokenIdsAsync()).Should().BeEmpty();
    }

    private RefreshToken Token(DateTime issuedAt, TimeSpan lifetime) =>
        RefreshToken.Issue(_userId, Guid.NewGuid(), Guid.NewGuid().ToString("N"), issuedAt, lifetime);

    private static LoginAttempt Attempt(string email, DateTime lastAttempt, DateTime? lockedUntil) =>
        new() { NormalizedEmail = email, FailedCount = 5, LastAttemptAt = lastAttempt, LockedUntil = lockedUntil };

    private async Task SaveAsync(params RefreshToken[] tokens)
    {
        await using var context = CreateContext();
        context.RefreshTokens.AddRange(tokens);
        await context.SaveChangesAsync();
    }

    private async Task<List<Guid>> TokenIdsAsync()
    {
        await using var context = CreateContext();

        return await context.RefreshTokens.Select(token => token.Id).ToListAsync();
    }

    private async Task<IReadOnlyDictionary<string, int>> PurgeAsync(int batchSize = 500)
    {
        await using var context = CreateContext();
        var purger = new IdentityRetentionPurger(
            context,
            _time,
            Options.Create(new AuthOptions()),
            Options.Create(new LockoutOptions()),
            Options.Create(new RetentionOptions { BatchSize = batchSize }));

        return await purger.PurgeAsync(CancellationToken.None);
    }

    private IdentityDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                _fixture.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable(IdentityDbContext.MigrationsHistoryTableName, IdentityDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options);
}
