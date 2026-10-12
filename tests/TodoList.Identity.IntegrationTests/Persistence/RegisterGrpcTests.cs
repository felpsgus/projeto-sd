using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TodoList.Contracts.Identity.V1;
using TodoList.Identity.Api.ResultMapping;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Persistence;
using Xunit;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// Cadastro de usuário via gRPC (BE-07) contra Postgres real (Testcontainers)
/// — CA-01 a CA-12. <b>Requer Docker.</b>
/// </summary>
[Collection("Postgres")]
public class RegisterGrpcTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;

    public RegisterGrpcTests(PostgresContainerFixture fixture)
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
                })));
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact] // CA-01, CA-02, CA-03
    [Trait("Category", "Docker")]
    public async Task Register_DadosValidos_CriaUsuarioComHashDiferenteDaSenha()
    {
        using var client = CreateClient();

        var response = await client.RegisterAsync(new RegisterRequest { Email = "ada@exemplo.com", Password = "senha123", DisplayName = "Ada" });

        response.Email.Should().Be("ada@exemplo.com");
        response.DisplayName.Should().Be("Ada");

        await using var context = CreateProbeContext();
        var user = await context.Users.SingleAsync(u => u.Id == Guid.Parse(response.Id));
        user.PasswordHash.Should().NotBe("senha123").And.NotBeNullOrEmpty();
    }

    [Fact] // CA-05
    [Trait("Category", "Docker")]
    public async Task Register_EmailJaExistente_RetornaFailedPreconditionComErrorCode()
    {
        using var client = CreateClient();
        await client.RegisterAsync(new RegisterRequest { Email = "joao@exemplo.com", Password = "senha123", DisplayName = "Joao" });

        var act = async () => await client.RegisterAsync(new RegisterRequest { Email = "joao@exemplo.com", Password = "outraSenha123", DisplayName = "Joao 2" });

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Which.Trailers.Get(ResultGrpcStatus.ErrorCodeTrailerKey)!.Value.Should().Be("auth.email_already_registered");
    }

    [Fact] // CA-06: caixa diferente também é conflito
    [Trait("Category", "Docker")]
    public async Task Register_EmailComCaixaDiferenteDeExistente_RetornaFailedPrecondition()
    {
        using var client = CreateClient();
        await client.RegisterAsync(new RegisterRequest { Email = "joao@exemplo.com", Password = "senha123", DisplayName = "Joao" });

        var act = async () => await client.RegisterAsync(new RegisterRequest { Email = "JOAO@Exemplo.com", Password = "outraSenha123", DisplayName = "" });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
    }

    [Fact] // CA-07
    [Trait("Category", "Docker")]
    public async Task Register_FormatoDeEmailInvalido_RetornaInvalidArgument()
    {
        using var client = CreateClient();

        var act = async () => await client.RegisterAsync(new RegisterRequest { Email = "nao-e-email", Password = "senha123", DisplayName = "" });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact] // CA-08
    [Trait("Category", "Docker")]
    public async Task Register_SenhaForaDaPolitica_RetornaInvalidArgument()
    {
        using var client = CreateClient();

        var act = async () => await client.RegisterAsync(new RegisterRequest { Email = "nova@exemplo.com", Password = "abcdefgh", DisplayName = "" });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact] // CA-12: duas requisições concorrentes com o mesmo e-mail — exatamente um usuário criado, a outra recebe Conflict, nunca 500
    [Trait("Category", "Docker")]
    public async Task Register_DuasRequisicoesConcorrentesMesmoEmail_ExatamenteUmaCriaUsuario()
    {
        using var client = CreateClient();
        const string email = "concorrente@exemplo.com";

        var task1 = SafeRegisterAsync(client, email);
        var task2 = SafeRegisterAsync(client, email);
        var results = await Task.WhenAll(task1, task2);

        results.Count(r => r.Succeeded).Should().Be(1);
        results.Count(r => !r.Succeeded && r.StatusCode == StatusCode.FailedPrecondition).Should().Be(1);

        await using var context = CreateProbeContext();
        var count = await context.Users.CountAsync(u => u.Email == Email.Create(email).Value);
        count.Should().Be(1);
    }

    private static async Task<(bool Succeeded, StatusCode StatusCode)> SafeRegisterAsync(IdentityGrpcTestClient client, string email)
    {
        try
        {
            await client.RegisterAsync(new RegisterRequest { Email = email, Password = "senha123", DisplayName = "" });
            return (true, StatusCode.OK);
        }
        catch (RpcException ex)
        {
            return (false, ex.StatusCode);
        }
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
