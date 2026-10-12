using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TodoList.Contracts.Identity.V1;
using TodoList.Identity.Application.Sessions;
using TodoList.Identity.Domain.Sessions;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Identity.Infrastructure.Security;
using Xunit;
using InfrastructurePersistence = TodoList.Identity.Infrastructure.Persistence.ServiceCollectionExtensions;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// Sessão completa (BE-10, BE-11, RN-AUTH-19) contra um servidor gRPC real e
/// Postgres real (Testcontainers): hash no banco, índice único, FK com cascata,
/// <b>dois redeems paralelos do mesmo token — exatamente um sucede</b>, logout,
/// troca de senha e exclusão de conta. <b>Requer Docker.</b> A mesma lógica roda
/// sem Docker, em SQLite, em <see cref="RefreshTokenSqliteTests"/>.
/// </summary>
[Collection("Postgres")]
public class RefreshTokenPostgresTests : IAsyncLifetime
{
    private const string Password = "senha-de-teste-123";
    private const string NewPassword = "outra-senha-456";

    private readonly Pbkdf2PasswordHasher _passwordHasher = new(Options.Create(new PasswordHashingOptions { Iterations = 200 }));
    private readonly PostgresContainerFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;

    public RefreshTokenPostgresTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.EnsureStartedAsync();

        await using (var context = CreateProbeContext())
        {
            await context.Database.EnsureDeletedAsync();
            await context.Database.MigrateAsync();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"ConnectionStrings:{InfrastructurePersistence.ConnectionStringName}"] = _fixture.ConnectionString,
                    ["UserStore:Provider"] = "Persisted",
                })));
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact] // CA-20, CA-17, FK cascata — o que a migration realmente criou
    [Trait("Category", "Docker")]
    public async Task Migration_CriaIndiceUnicoEmTokenHashIndiceCompostoEFkComCascata()
    {
        await using var context = CreateProbeContext();

        var indexes = await context.Database
            .SqlQueryRaw<string>("select indexdef as \"Value\" from pg_indexes where schemaname = 'identity' and tablename = 'refresh_tokens'")
            .ToListAsync();
        var onDelete = await context.Database
            .SqlQueryRaw<string>(
                "select confdeltype::text as \"Value\" from pg_constraint "
                + "where conrelid = 'identity.refresh_tokens'::regclass and contype = 'f'")
            .ToListAsync();

        indexes.Should().Contain(definition => definition.Contains("UNIQUE", StringComparison.Ordinal) && definition.Contains("(token_hash)", StringComparison.Ordinal));
        indexes.Should().Contain(definition => definition.Contains("(user_id, session_id)", StringComparison.Ordinal));
        onDelete.Should().ContainSingle().Which.Should().Be("c", "ON DELETE CASCADE");
    }

    [Fact] // CA-17: o banco guarda o hash, nunca o valor
    [Trait("Category", "Docker")]
    public async Task Login_GuardaSoOHashDoRefreshToken()
    {
        var email = await CreateUserAsync();
        using var client = CreateClient();

        var login = await LoginAsync(client, email);

        await using var context = CreateProbeContext();
        var stored = await context.RefreshTokens.AsNoTracking().SingleAsync();
        stored.TokenHash.Should().Be(RefreshTokenSecret.Hash(login.RefreshToken));
        (await context.RefreshTokens.AnyAsync(token => token.TokenHash == login.RefreshToken)).Should().BeFalse();
    }

    [Fact] // CA-12: dois refreshes paralelos com o mesmo token — exatamente um sucede
    [Trait("Category", "Docker")]
    public async Task RefreshSession_DoisRedeemsParalelosDoMesmoToken_ExatamenteUmSucede()
    {
        var email = await CreateUserAsync();

        for (var round = 0; round < 5; round++)
        {
            using var loginClient = CreateClient();
            var login = await LoginAsync(loginClient, email);
            using var first = CreateClient();
            using var second = CreateClient();

            var responses = await Task.WhenAll(
                first.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = login.RefreshToken }).ResponseAsync,
                second.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = login.RefreshToken }).ResponseAsync);

            responses.Count(response => response.Succeeded).Should().Be(1, $"rodada {round}");

            await using var context = CreateProbeContext();
            var loginHash = RefreshTokenSecret.Hash(login.RefreshToken);
            var sessionId = (await context.RefreshTokens.AsNoTracking().SingleAsync(token => token.TokenHash == loginHash)).SessionId;
            var session = await context.RefreshTokens.AsNoTracking().Where(token => token.SessionId == sessionId).ToListAsync();

            session.Count(token => token.ConsumedAt != null).Should().Be(1, "o pai é consumido uma única vez");
            session.Count(token => token.RevokedAt == null).Should().BeLessThanOrEqualTo(1, "nunca dois tokens ativos derivados do mesmo pai");
        }
    }

    [Fact] // BE-10 CA-08/CA-09: reuso derruba a cadeia, inclusive o token legítimo mais novo
    [Trait("Category", "Docker")]
    public async Task RefreshSession_Reuso_RevogaACadeiaInteira()
    {
        var email = await CreateUserAsync();
        using var client = CreateClient();
        var login = await LoginAsync(client, email);
        var rotated = await client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = login.RefreshToken });

        var reuse = await client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = login.RefreshToken });
        var legitimate = await client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = rotated.RefreshToken });

        rotated.Succeeded.Should().BeTrue();
        reuse.Succeeded.Should().BeFalse();
        legitimate.Succeeded.Should().BeFalse();

        await using var context = CreateProbeContext();
        (await context.RefreshTokens.AsNoTracking().ToListAsync()).Should()
            .OnlyContain(token => token.RevokedReason == RefreshTokenRevocationReason.ReuseDetected);
    }

    [Fact] // BE-11 CA-03..CA-05: logout grava RevokedAt/Logout, derruba a cadeia, poupa outra sessão
    [Trait("Category", "Docker")]
    public async Task Logout_RevogaASessaoNoBancoEPoupaAsOutras()
    {
        var email = await CreateUserAsync();
        using var client = CreateClient();
        var sessionA = await LoginAsync(client, email);
        var sessionB = await LoginAsync(client, email);
        var rotatedA = await client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = sessionA.RefreshToken });

        await client.LogoutAsync(new LogoutRequest { UserId = sessionA.UserId, RefreshToken = rotatedA.RefreshToken });

        (await client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = rotatedA.RefreshToken })).Succeeded.Should().BeFalse();
        (await client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = sessionB.RefreshToken })).Succeeded.Should().BeTrue();

        await using var context = CreateProbeContext();
        var tokens = await context.RefreshTokens.AsNoTracking().ToListAsync();
        tokens.Where(token => token.RevokedReason == RefreshTokenRevocationReason.Logout).Should().HaveCount(2, "a cadeia da sessão A inteira");
    }

    [Fact] // BE-11 CA-09: token de outro usuário não revoga nada
    [Trait("Category", "Docker")]
    public async Task Logout_TokenDeOutroUsuario_NaoRevogaASessaoAlheia()
    {
        var victimEmail = await CreateUserAsync();
        var attackerEmail = await CreateUserAsync();
        using var client = CreateClient();
        var victim = await LoginAsync(client, victimEmail);
        var attacker = await LoginAsync(client, attackerEmail);

        await client.LogoutAsync(new LogoutRequest { UserId = attacker.UserId, RefreshToken = victim.RefreshToken });

        (await client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = victim.RefreshToken })).Succeeded.Should().BeTrue();
    }

    [Fact] // BE-11 CA-06
    [Trait("Category", "Docker")]
    public async Task LogoutAll_RevogaTodasAsSessoesDoUsuario()
    {
        var email = await CreateUserAsync();
        using var client = CreateClient();
        var sessionA = await LoginAsync(client, email);
        var sessionB = await LoginAsync(client, email);

        await client.LogoutAllAsync(new LogoutAllRequest { UserId = sessionA.UserId });

        (await client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = sessionA.RefreshToken })).Succeeded.Should().BeFalse();
        (await client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = sessionB.RefreshToken })).Succeeded.Should().BeFalse();
    }

    [Fact] // RN-AUTH-19: troca de senha revoga as sessões (BE-15 CA-07..CA-09)
    [Trait("Category", "Docker")]
    public async Task ChangePassword_RevogaTodasAsSessoes()
    {
        var email = await CreateUserAsync();
        using var client = CreateClient();
        var sessionA = await LoginAsync(client, email);
        var sessionB = await LoginAsync(client, email);

        await client.ChangePasswordAsync(new ChangePasswordRequest { UserId = sessionA.UserId, CurrentPassword = Password, NewPassword = NewPassword });

        (await client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = sessionA.RefreshToken })).Succeeded.Should().BeFalse();
        (await client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = sessionB.RefreshToken })).Succeeded.Should().BeFalse();

        await using var context = CreateProbeContext();
        (await context.RefreshTokens.AsNoTracking().ToListAsync()).Should()
            .OnlyContain(token => token.RevokedReason == RefreshTokenRevocationReason.PasswordChanged);
    }

    [Fact] // RN-AUTH-19 / BE-16: a cascata da FK remove os refresh tokens
    [Trait("Category", "Docker")]
    public async Task DeleteAccount_RemoveOsRefreshTokensPorCascata()
    {
        var email = await CreateUserAsync();
        using var client = CreateClient();
        var login = await LoginAsync(client, email);
        await client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = login.RefreshToken });

        await client.DeleteAccountAsync(new DeleteAccountRequest { UserId = login.UserId, Password = Password });

        await using var context = CreateProbeContext();
        (await context.RefreshTokens.CountAsync()).Should().Be(0);
        (await client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = login.RefreshToken })).Succeeded.Should().BeFalse();
    }

    private static async Task<LoginResponse> LoginAsync(IdentityGrpcTestClient client, string email)
    {
        var login = await client.LoginAsync(new LoginRequest { Email = email, Password = Password });
        login.Succeeded.Should().BeTrue();

        return login;
    }

    private async Task<string> CreateUserAsync()
    {
        var email = $"sessao-{Guid.NewGuid():N}@todolist.example";

        await using var context = CreateProbeContext();
        var user = User.Create(Email.Create(email).Value, "Sessao", _passwordHasher.Hash(Password), TimeProvider.System).Value;
        context.Users.Add(user);
        await context.SaveChangesAsync();

        return email;
    }

    private IdentityGrpcTestClient CreateClient() => new(_factory.Server.CreateHandler(), _factory.Server.BaseAddress);

    private IdentityDbContext CreateProbeContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                _fixture.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable(IdentityDbContext.MigrationsHistoryTableName, IdentityDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new IdentityDbContext(options);
    }
}
