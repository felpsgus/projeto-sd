using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TodoList.Contracts.Identity.V1;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Identity.Infrastructure.Security;
using Xunit;
using InfrastructurePersistence = TodoList.Identity.Infrastructure.Persistence.ServiceCollectionExtensions;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// BE-12 (RN-AUTH-13) de ponta a ponta: RPC <c>Login</c> real, Postgres real
/// (Testcontainers, <b>requer Docker</b>) e <see cref="FakeTimeProvider"/> — o relógio
/// avança, nunca se dorme. Prova o upsert atômico (CA-10) e a persistência (CA-11).
/// </summary>
[Collection("Postgres")]
public class LoginLockoutPostgresTests : IAsyncLifetime
{
    private const string Password = "senha-de-teste-123";
    private const string AdaEmail = "ada@lockout.example";
    private const string GraceEmail = "grace@lockout.example";

    private readonly Pbkdf2PasswordHasher _passwordHasher = new(Options.Create(new PasswordHashingOptions { Iterations = 200 }));
    private readonly PostgresContainerFixture _fixture;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-03T10:00:00Z"));
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public LoginLockoutPostgresTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.EnsureStartedAsync();

        await using var context = CreateProbeContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();

        context.Users.Add(NewUser(AdaEmail, "Ada"));
        context.Users.Add(NewUser(GraceEmail, "Grace"));
        await context.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _factories.ForEach(factory => factory.Dispose());

        return Task.CompletedTask;
    }

    [Fact] // CA-01, CA-02, CA-03, CA-04
    [Trait("Category", "Docker")]
    public async Task Login_CincoFalhasBloqueiam_SextaRecusadaComTempoRestanteEExpiraComORelogio()
    {
        using var client = CreateClient(CreateFactory());

        await FailAsync(client, AdaEmail, 5);

        var blocked = await Login(client, AdaEmail, Password);
        blocked.Succeeded.Should().BeFalse();
        blocked.LockedOut.Should().BeTrue();
        blocked.RetryAfterSeconds.Should().Be(15 * 60);
        blocked.AccessToken.Should().BeEmpty();
        blocked.RefreshToken.Should().BeEmpty();

        _time.Advance(TimeSpan.FromMinutes(10));
        (await Login(client, AdaEmail, Password)).RetryAfterSeconds.Should().Be(5 * 60);

        _time.Advance(TimeSpan.FromMinutes(5));
        (await Login(client, AdaEmail, Password)).Succeeded.Should().BeTrue();
    }

    [Fact] // CA-05
    [Trait("Category", "Docker")]
    public async Task Login_SucessoZeraOContador()
    {
        using var client = CreateClient(CreateFactory());

        await FailAsync(client, AdaEmail, 4);
        (await Login(client, AdaEmail, Password)).Succeeded.Should().BeTrue();
        await FailAsync(client, AdaEmail, 5);

        (await Login(client, AdaEmail, Password)).LockedOut.Should().BeTrue();
    }

    [Fact] // CA-06, CA-07, CA-08
    [Trait("Category", "Docker")]
    public async Task Login_BloqueioPorEmailNormalizado_ValeParaInexistenteENaoAfetaOutros()
    {
        using var client = CreateClient(CreateFactory());

        await FailAsync(client, "ADA@Lockout.example", 3);
        await FailAsync(client, " ada@lockout.example ", 2);
        await FailAsync(client, "ninguem@lockout.example", 5);

        (await Login(client, AdaEmail, Password)).LockedOut.Should().BeTrue();
        (await Login(client, "ninguem@lockout.example", Password)).LockedOut.Should().BeTrue();
        (await Login(client, GraceEmail, Password)).Succeeded.Should().BeTrue();
    }

    [Fact] // CA-09
    [Trait("Category", "Docker")]
    public async Task Login_TentativasEspacadasAlemDaJanela_NaoAcumulam()
    {
        using var client = CreateClient(CreateFactory());

        await FailAsync(client, AdaEmail, 4);
        _time.Advance(TimeSpan.FromMinutes(20));
        await FailAsync(client, AdaEmail, 2);

        (await Login(client, AdaEmail, Password)).Succeeded.Should().BeTrue();
    }

    [Fact] // CA-10: 10 concorrentes com senha errada => exatamente MaxAttempts verificações
    [Trait("Category", "Docker")]
    public async Task Login_DezTentativasConcorrentes_NoMaximoMaxAttemptsSaoProcessadas()
    {
        using var client = CreateClient(CreateFactory());

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Login(client, AdaEmail, "errada")));

        responses.Count(response => !response.LockedOut).Should().Be(5);
        responses.Count(response => response.LockedOut).Should().Be(5);
        (await Login(client, AdaEmail, Password)).LockedOut.Should().BeTrue();
    }

    [Fact] // CA-11: o contador está no banco — uma instância nova da aplicação enxerga o bloqueio
    [Trait("Category", "Docker")]
    public async Task Login_BloqueioSobreviveAReinicioDaAplicacao()
    {
        using (var before = CreateClient(CreateFactory()))
        {
            await FailAsync(before, AdaEmail, 5);
        }

        using var after = CreateClient(CreateFactory());

        (await Login(after, AdaEmail, Password)).LockedOut.Should().BeTrue();
    }

    [Fact] // CA-12
    [Trait("Category", "Docker")]
    public async Task Login_LockoutDesabilitado_NuncaBloqueiaNemGravaContador()
    {
        using var client = CreateClient(CreateFactory(("Lockout:Enabled", "false")));

        await FailAsync(client, AdaEmail, 10);

        (await Login(client, AdaEmail, Password)).Succeeded.Should().BeTrue();
        await using var context = CreateProbeContext();
        (await context.LoginAttempts.CountAsync()).Should().Be(0);
    }

    [Fact] // CA-13: só configuração
    [Trait("Category", "Docker")]
    public async Task Login_MaxAttemptsTresPorConfiguracao_BloqueiaNaQuartaTentativa()
    {
        using var client = CreateClient(CreateFactory(("Lockout:MaxAttempts", "3")));

        await FailAsync(client, AdaEmail, 3);

        (await Login(client, AdaEmail, Password)).LockedOut.Should().BeTrue();
    }

    private static async Task FailAsync(IdentityGrpcTestClient client, string email, int times)
    {
        for (var i = 0; i < times; i++)
        {
            (await Login(client, email, "errada")).LockedOut.Should().BeFalse($"a tentativa {i + 1} ainda é processada");
        }
    }

    private static async Task<LoginResponse> Login(IdentityGrpcTestClient client, string email, string password) =>
        await client.LoginAsync(new LoginRequest { Email = email, Password = password });

    private static IdentityGrpcTestClient CreateClient(WebApplicationFactory<Program> factory) =>
        new(factory.Server.CreateHandler(), factory.Server.BaseAddress);

    private User NewUser(string email, string displayName) =>
        User.Create(Domain.Users.Email.Create(email).Value, displayName, _passwordHasher.Hash(Password), TimeProvider.System).Value;

    private WebApplicationFactory<Program> CreateFactory(params (string Key, string Value)[] settings)
    {
        var configuration = new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{InfrastructurePersistence.ConnectionStringName}"] = _fixture.ConnectionString,
            ["UserStore:Provider"] = "Persisted",
            ["PasswordHashing:Iterations"] = "200",
        };

        foreach (var (key, value) in settings)
        {
            configuration[key] = value;
        }

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(_time));
        });
        _factories.Add(factory);

        return factory;
    }

    private IdentityDbContext CreateProbeContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                _fixture.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable(IdentityDbContext.MigrationsHistoryTableName, IdentityDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options);
}
