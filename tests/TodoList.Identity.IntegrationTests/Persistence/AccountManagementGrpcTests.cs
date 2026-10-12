using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TodoList.Contracts.Identity.V1;
using TodoList.Identity.Api.ResultMapping;
using TodoList.Identity.Infrastructure.Persistence;
using Xunit;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// Perfil (BE-14) e troca de senha (BE-15) via gRPC contra Postgres real
/// (Testcontainers). <b>Requer Docker.</b>
/// </summary>
[Collection("Postgres")]
public class AccountManagementGrpcTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;
    private readonly FailingSaveChangesInterceptor _failing = new();

    public AccountManagementGrpcTests(PostgresContainerFixture fixture)
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
                }))
            .ConfigureServices(services => services.ConfigureDbContext<IdentityDbContext>(options => options.AddInterceptors(_failing))));
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact] // BE-14, CA-01/CA-04/CA-05/CA-08/CA-09/CA-11
    [Trait("Category", "Docker")]
    public async Task GetProfileEUpdateProfile_FluxoCompleto_RefleteNomeAtualizadoEIsolaUsuarios()
    {
        using var client = CreateClient();
        var userA = await client.RegisterAsync(new RegisterRequest { Email = "a@exemplo.com", Password = "senha123", DisplayName = "A" });
        var userB = await client.RegisterAsync(new RegisterRequest { Email = "b@exemplo.com", Password = "senha123", DisplayName = "B" });

        var updated = await client.UpdateProfileAsync(new UpdateProfileRequest { UserId = userA.Id, DisplayName = "  Novo Nome A  " });
        updated.DisplayName.Should().Be("Novo Nome A");
        // CA-11: CreatedAt não muda — comparado com tolerância de 1ms porque o
        // Postgres `timestamp` guarda microssegundos e o valor de userA.CreatedAt
        // veio direto do domínio (TimeProvider, sem passar pelo banco ainda),
        // com precisão de tick (100ns) que o round-trip pelo banco trunca.
        updated.CreatedAt.ToDateTime().Should().BeCloseTo(userA.CreatedAt.ToDateTime(), TimeSpan.FromMilliseconds(1));

        var profileA = await client.GetProfileAsync(new GetProfileRequest { UserId = userA.Id });
        var profileB = await client.GetProfileAsync(new GetProfileRequest { UserId = userB.Id });

        profileA.DisplayName.Should().Be("Novo Nome A");
        profileA.Email.Should().Be("a@exemplo.com");
        profileB.DisplayName.Should().Be("B", "CA-04: cada usuário só vê/altera os próprios dados");
        profileB.Email.Should().Be("b@exemplo.com");
    }

    [Fact] // BE-14, CA-09: e-mail permanece inalterado — a mensagem UpdateProfileRequest nem tem esse campo
    [Trait("Category", "Docker")]
    public async Task UpdateProfile_NuncaAlteraEmail()
    {
        using var client = CreateClient();
        var user = await client.RegisterAsync(new RegisterRequest { Email = "imutavel@exemplo.com", Password = "senha123", DisplayName = "Original" });

        await client.UpdateProfileAsync(new UpdateProfileRequest { UserId = user.Id, DisplayName = "Alterado" });

        await using var context = CreateProbeContext();
        var persisted = await context.Users.SingleAsync(u => u.Id == Guid.Parse(user.Id));
        persisted.Email.Value.Should().Be("imutavel@exemplo.com");
    }

    [Fact] // BE-15: fluxo completo login -> troca -> login com senha antiga falha -> login com senha nova funciona
    [Trait("Category", "Docker")]
    public async Task ChangePassword_FluxoCompleto_SenhaAntigaParaDeFuncionarENovaFunciona()
    {
        using var client = CreateClient();
        const string oldPassword = "senha-antiga-123";
        const string newPassword = "senha-nova-456";
        var user = await client.RegisterAsync(new RegisterRequest { Email = "troca@exemplo.com", Password = oldPassword, DisplayName = "" });

        var loginAntes = await client.LoginAsync(new LoginRequest { Email = "troca@exemplo.com", Password = oldPassword });
        loginAntes.Succeeded.Should().BeTrue();

        await client.ChangePasswordAsync(new ChangePasswordRequest { UserId = user.Id, CurrentPassword = oldPassword, NewPassword = newPassword });

        var loginComSenhaAntiga = await client.LoginAsync(new LoginRequest { Email = "troca@exemplo.com", Password = oldPassword });
        loginComSenhaAntiga.Succeeded.Should().BeFalse("CA-03: senha antiga não funciona mais");

        var loginComSenhaNova = await client.LoginAsync(new LoginRequest { Email = "troca@exemplo.com", Password = newPassword });
        loginComSenhaNova.Succeeded.Should().BeTrue("CA-02: senha nova funciona");
    }

    [Fact] // BE-15, CA-10: sessões/contas de outros usuários não são afetadas pela troca
    [Trait("Category", "Docker")]
    public async Task ChangePassword_NaoAfetaOutroUsuario()
    {
        using var client = CreateClient();
        var userA = await client.RegisterAsync(new RegisterRequest { Email = "a2@exemplo.com", Password = "senha-a-123", DisplayName = "" });
        await client.RegisterAsync(new RegisterRequest { Email = "b2@exemplo.com", Password = "senha-b-123", DisplayName = "" });

        await client.ChangePasswordAsync(new ChangePasswordRequest { UserId = userA.Id, CurrentPassword = "senha-a-123", NewPassword = "senha-a-nova-456" });

        var loginB = await client.LoginAsync(new LoginRequest { Email = "b2@exemplo.com", Password = "senha-b-123" });
        loginB.Succeeded.Should().BeTrue();
    }

    [Fact] // BE-15, CA-04: senha atual incorreta não altera o hash no banco
    [Trait("Category", "Docker")]
    public async Task ChangePassword_SenhaAtualIncorreta_NaoAlteraHash()
    {
        using var client = CreateClient();
        var user = await client.RegisterAsync(new RegisterRequest { Email = "atual-errada@exemplo.com", Password = "senha-correta-123", DisplayName = "" });

        await using var contextAntes = CreateProbeContext();
        var hashAntes = (await contextAntes.Users.SingleAsync(u => u.Id == Guid.Parse(user.Id))).PasswordHash;

        var act = async () => await client.ChangePasswordAsync(
            new ChangePasswordRequest { UserId = user.Id, CurrentPassword = "senha-errada", NewPassword = "senha-nova-456" });

        // Decisão do tech lead (onda 2 da Fase 3): 400 (InvalidArgument), não 401 —
        // a requisição já está autenticada; o que falhou é o campo currentPassword.
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        exception.Which.Trailers.Get(ResultGrpcStatus.ErrorCodeTrailerKey)!.Value.Should().Be("auth.invalid_current_password");

        var validationErrorsJson = exception.Which.Trailers.Get(ResultGrpcStatus.ValidationErrorsTrailerKey)!.Value;
        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string[]>>(validationErrorsJson)!
            .Should().ContainKey("CurrentPassword");

        await using var contextDepois = CreateProbeContext();
        var hashDepois = (await contextDepois.Users.SingleAsync(u => u.Id == Guid.Parse(user.Id))).PasswordHash;
        hashDepois.Should().Be(hashAntes);
    }

    [Fact] // BE-15, CA-10: o refresh token de B continua válido depois que A troca a senha
    [Trait("Category", "Docker")]
    public async Task ChangePassword_NaoRevogaRefreshTokenDeOutroUsuario()
    {
        using var client = CreateClient();
        var userA = await client.RegisterAsync(new RegisterRequest { Email = "a3@exemplo.com", Password = "senha-a-123", DisplayName = "" });
        await client.RegisterAsync(new RegisterRequest { Email = "b3@exemplo.com", Password = "senha-b-123", DisplayName = "" });
        var loginB = await client.LoginAsync(new LoginRequest { Email = "b3@exemplo.com", Password = "senha-b-123" });

        await client.ChangePasswordAsync(new ChangePasswordRequest { UserId = userA.Id, CurrentPassword = "senha-a-123", NewPassword = "senha-a-nova-456" });

        (await client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = loginB.RefreshToken })).Succeeded.Should().BeTrue();
    }

    [Fact] // BE-15, CA-11/CA-12: falha na persistência -> hash intacto E sessões não revogadas (mesmo SaveChanges)
    [Trait("Category", "Docker")]
    public async Task ChangePassword_FalhaNaPersistencia_NaoMudaHashNemRevogaSessoes()
    {
        using var client = CreateClient();
        var user = await client.RegisterAsync(new RegisterRequest { Email = "falha@exemplo.com", Password = "senha-antiga-123", DisplayName = "" });
        var login = await client.LoginAsync(new LoginRequest { Email = "falha@exemplo.com", Password = "senha-antiga-123" });
        _failing.Armed = true;

        var act = async () => await client.ChangePasswordAsync(
            new ChangePasswordRequest { UserId = user.Id, CurrentPassword = "senha-antiga-123", NewPassword = "senha-nova-456" });
        await act.Should().ThrowAsync<RpcException>();
        _failing.Armed = false;

        (await client.LoginAsync(new LoginRequest { Email = "falha@exemplo.com", Password = "senha-antiga-123" })).Succeeded.Should().BeTrue("hash não mudou");
        (await client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = login.RefreshToken })).Succeeded.Should().BeTrue("sessão não foi revogada");
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
