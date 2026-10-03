using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TodoList.Contracts.Identity.V1;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Identity.Infrastructure.Security;
using Xunit;
using InfrastructurePersistence = TodoList.Identity.Infrastructure.Persistence.ServiceCollectionExtensions;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// <c>Login</c> contra um servidor gRPC real (BE-33 CA-01/CA-08), com
/// <c>UserStore:Provider=Persisted</c> e Postgres real (Testcontainers).
/// <b>Requer Docker.</b>
///
/// <para>
/// Onda E (T2): o cenário não usa mais <c>DemoUserSeeder</c> (removido — o
/// seed de demonstração saiu do produto, ver <see cref="TodoList.Identity.Api.Configuration.UserStoreOptions"/>).
/// Os usuários de teste são criados diretamente pelo repositório, do mesmo
/// jeito que o próprio cadastro real (<c>RegisterUserHandler</c>) os criaria
/// — só sem passar pelo gRPC, para manter o teste focado em Login.
/// </para>
/// </summary>
[Collection("Postgres")]
public class LoginGrpcTests : IAsyncLifetime
{
    private const string DemoPassword = "senha-de-demonstracao-para-teste-123";
    private const string UserEmail = "ada.lovelace@todolist.example";

    private readonly Pbkdf2PasswordHasher _passwordHasher = new(Options.Create(new PasswordHashingOptions { Iterations = 200 }));
    private readonly PostgresContainerFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;
    private Guid _userId;

    public LoginGrpcTests(PostgresContainerFixture fixture)
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

        _userId = await CreateUserAsync(UserEmail, "Ada Lovelace");

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

    [Fact] // BE-33 CA-01
    [Trait("Category", "Docker")]
    public async Task Login_UsuarioComSenhaCorreta_RetornaAccessToken()
    {
        using var client = CreateClient();

        var login = await client.LoginAsync(new LoginRequest { Email = UserEmail, Password = DemoPassword });

        login.Succeeded.Should().BeTrue();
        login.AccessToken.Should().NotBeNullOrEmpty();
        login.UserId.Should().Be(_userId.ToString());
        login.ExpiresAt.Should().NotBeNull();
    }

    private async Task<Guid> CreateUserAsync(string email, string displayName)
    {
        await using var context = CreateProbeContext();

        var emailResult = Email.Create(email);
        var userResult = User.Create(emailResult.Value, displayName, _passwordHasher.Hash(DemoPassword), TimeProvider.System);
        var user = userResult.Value;

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return user.Id;
    }

    private IdentityGrpcTestClient CreateClient()
    {
        var handler = _factory.Server.CreateHandler();
        return new IdentityGrpcTestClient(handler, _factory.Server.BaseAddress);
    }

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
