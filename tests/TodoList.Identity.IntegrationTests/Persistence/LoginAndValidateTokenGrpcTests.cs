using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TodoList.Contracts.Identity.V1;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Identity.Infrastructure.Users;
using Xunit;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// Fluxo ponta a ponta <c>Login</c> → <c>ValidateToken</c> contra um servidor
/// gRPC real (BE-33 CA-01/CA-08, BE-34 CA-01/CA-08/CA-09/CA-10), com
/// <c>UserStore:Provider=Persisted</c>, seed ligado e Postgres real
/// (Testcontainers). <b>Requer Docker.</b>
/// </summary>
[Collection("Postgres")]
public class LoginAndValidateTokenGrpcTests : IAsyncLifetime
{
    private const string DemoPassword = "senha-de-demonstracao-para-teste-123";

    private readonly PostgresContainerFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;

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

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"ConnectionStrings:{ServiceCollectionExtensions.ConnectionStringName}"] = _fixture.ConnectionString,
                    ["UserStore:Provider"] = "Persisted",
                    ["UserStore:SeedDemoUsers"] = "true",
                    ["UserStore:DemoUserPassword"] = DemoPassword,
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

        var login = await client.LoginAsync(new LoginRequest { Email = "ada.lovelace@todolist.example", Password = DemoPassword });

        login.Succeeded.Should().BeTrue();
        login.AccessToken.Should().NotBeNullOrEmpty();
        login.UserId.Should().Be(InMemoryUserLookup.ActiveUserId.ToString());
        login.ExpiresAt.Should().NotBeNull();

        var validation = await client.ValidateTokenAsync(new ValidateTokenRequest { AccessToken = login.AccessToken });

        validation.Valid.Should().BeTrue();
        validation.UserId.Should().Be(InMemoryUserLookup.ActiveUserId.ToString());
    }

    [Fact] // BE-33 — usuário inativo do seed nunca autentica, mesmo com a senha correta
    [Trait("Category", "Docker")]
    public async Task Login_UsuarioInativoComSenhaCorreta_RetornaSucceededFalse()
    {
        using var client = CreateClient();

        var response = await client.LoginAsync(new LoginRequest { Email = "charles.babbage@todolist.example", Password = DemoPassword });

        response.Succeeded.Should().BeFalse();
        response.AccessToken.Should().BeEmpty();
    }

    [Fact] // BE-34 CA-09 — ValidateToken não consulta o store de usuários: token continua válido mesmo com o usuário já excluído do banco
    [Trait("Category", "Docker")]
    public async Task ValidateToken_ComUsuarioRemovidoDoBancoDepoisDoLogin_ContinuaValido()
    {
        using var client = CreateClient();
        var login = await client.LoginAsync(new LoginRequest { Email = "ada.lovelace@todolist.example", Password = DemoPassword });
        login.Succeeded.Should().BeTrue();

        await using (var context = CreateProbeContext())
        {
            var user = await context.Users.SingleAsync(u => u.Id == InMemoryUserLookup.ActiveUserId);
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
        var login = await client.LoginAsync(new LoginRequest { Email = "ada.lovelace@todolist.example", Password = DemoPassword });
        login.Succeeded.Should().BeTrue();

        await using (var context = CreateProbeContext())
        {
            var user = await context.Users.SingleAsync(u => u.Id == InMemoryUserLookup.ActiveUserId);
            user.Deactivate(TimeProvider.System);
            await context.SaveChangesAsync();
        }

        var validation = await client.ValidateTokenAsync(new ValidateTokenRequest { AccessToken = login.AccessToken });

        validation.Valid.Should().BeTrue("BE-34 não checa IsActive — consequência aceita, não regressão (RN-USER-04 continua garantida em BE-28)");
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
