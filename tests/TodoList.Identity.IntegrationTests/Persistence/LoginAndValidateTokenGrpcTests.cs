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
/// Fluxo ponta a ponta <c>Login</c> → <c>ValidateToken</c> contra um servidor
/// gRPC real (BE-33 CA-01/CA-08, BE-34 CA-01/CA-08/CA-09/CA-10), com
/// <c>UserStore:Provider=Persisted</c> e Postgres real (Testcontainers).
/// <b>Requer Docker.</b>
///
/// <para>
/// Onda E (T2): o cenário não usa mais <c>DemoUserSeeder</c> (removido — o
/// seed de demonstração saiu do produto, ver <see cref="TodoList.Identity.Api.Configuration.UserStoreOptions"/>).
/// Os usuários de teste são criados diretamente pelo repositório, do mesmo
/// jeito que o próprio cadastro real (<c>RegisterUserHandler</c>) os criaria
/// — só sem passar pelo gRPC, para manter o teste focado em Login/ValidateToken.
/// </para>
/// </summary>
[Collection("Postgres")]
public class LoginAndValidateTokenGrpcTests : IAsyncLifetime
{
    private const string DemoPassword = "senha-de-demonstracao-para-teste-123";
    private const string ActiveUserEmail = "ada.lovelace@todolist.example";
    private const string InactiveUserEmail = "charles.babbage@todolist.example";

    private readonly Pbkdf2PasswordHasher _passwordHasher = new(Options.Create(new PasswordHashingOptions { Iterations = 200 }));
    private readonly PostgresContainerFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;
    private Guid _activeUserId;
    private Guid _inactiveUserId;

    public LoginAndValidateTokenGrpcTests(PostgresContainerFixture fixture)
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

        _activeUserId = await CreateUserAsync(ActiveUserEmail, "Ada Lovelace", isActive: true);
        _inactiveUserId = await CreateUserAsync(InactiveUserEmail, "Charles Babbage", isActive: false);

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

    [Fact] // BE-33 CA-01, BE-34 CA-01 — fluxo ponta a ponta
    [Trait("Category", "Docker")]
    public async Task Login_UsuarioAtivoComSenhaCorreta_GeraAccessTokenValidoParaValidateToken()
    {
        using var client = CreateClient();

        var login = await client.LoginAsync(new LoginRequest { Email = ActiveUserEmail, Password = DemoPassword });

        login.Succeeded.Should().BeTrue();
        login.AccessToken.Should().NotBeNullOrEmpty();
        login.UserId.Should().Be(_activeUserId.ToString());
        login.ExpiresAt.Should().NotBeNull();

        var validation = await client.ValidateTokenAsync(new ValidateTokenRequest { AccessToken = login.AccessToken });

        validation.Valid.Should().BeTrue();
        validation.UserId.Should().Be(_activeUserId.ToString());
    }

    [Fact] // BE-33 — usuário inativo nunca autentica, mesmo com a senha correta
    [Trait("Category", "Docker")]
    public async Task Login_UsuarioInativoComSenhaCorreta_RetornaSucceededFalse()
    {
        using var client = CreateClient();

        var response = await client.LoginAsync(new LoginRequest { Email = InactiveUserEmail, Password = DemoPassword });

        response.Succeeded.Should().BeFalse();
        response.AccessToken.Should().BeEmpty();
    }

    [Fact] // BE-34 CA-09 — ValidateToken não consulta o store de usuários: token continua válido mesmo com o usuário já excluído do banco
    [Trait("Category", "Docker")]
    public async Task ValidateToken_ComUsuarioRemovidoDoBancoDepoisDoLogin_ContinuaValido()
    {
        using var client = CreateClient();
        var login = await client.LoginAsync(new LoginRequest { Email = ActiveUserEmail, Password = DemoPassword });
        login.Succeeded.Should().BeTrue();

        await using (var context = CreateProbeContext())
        {
            var user = await context.Users.SingleAsync(u => u.Id == _activeUserId);
            context.Users.Remove(user);
            await context.SaveChangesAsync();
        }

        var validation = await client.ValidateTokenAsync(new ValidateTokenRequest { AccessToken = login.AccessToken });

        validation.Valid.Should().BeTrue("ValidateToken não consulta o store de usuários (CA-09 de BE-34)");
    }

    [Fact] // BE-34 CA-10 — usuário desativado depois do login continua valid=true até o token expirar (consequência aceita, não regressão)
    [Trait("Category", "Docker")]
    public async Task ValidateToken_UsuarioDesativadoDepoisDoLogin_ContinuaValidoDentroDaValidade()
    {
        using var client = CreateClient();
        var login = await client.LoginAsync(new LoginRequest { Email = ActiveUserEmail, Password = DemoPassword });
        login.Succeeded.Should().BeTrue();

        await using (var context = CreateProbeContext())
        {
            var user = await context.Users.SingleAsync(u => u.Id == _activeUserId);
            user.Deactivate(TimeProvider.System);
            await context.SaveChangesAsync();
        }

        var validation = await client.ValidateTokenAsync(new ValidateTokenRequest { AccessToken = login.AccessToken });

        validation.Valid.Should().BeTrue("BE-34 não checa IsActive — consequência aceita, não regressão (RN-USER-04 continua garantida em BE-28)");
    }

    private async Task<Guid> CreateUserAsync(string email, string displayName, bool isActive)
    {
        await using var context = CreateProbeContext();

        var emailResult = Email.Create(email);
        var userResult = User.Create(emailResult.Value, displayName, _passwordHasher.Hash(DemoPassword), TimeProvider.System);
        var user = userResult.Value;

        if (!isActive)
        {
            user.Deactivate(TimeProvider.System);
        }

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
