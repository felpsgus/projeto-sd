extern alias IdentityApi;

using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TodoList.Contracts.Identity.V1;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Identity.Infrastructure.Users;
using TodoList.Tasks.Infrastructure.Persistence;
using TodoList.Tasks.IntegrationTests.Persistence;
using Xunit;
using IdentityProgram = IdentityApi::Program;
using ProtoCreateTaskRequest = TodoList.Contracts.Tasks.V1.CreateTaskRequest;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// BE-17 (CA-23), BE-28 (CA-04, CA-06, CA-09), migrado para gRPC por BE-35 —
/// prova, contra <b>Postgres real</b> (Testcontainers, não o SQLite in-memory
/// do resto desta pasta), que os três desfechos de rejeição de
/// <c>CreateTask</c> não gravam nenhuma linha em <c>tasks.tasks</c>: dono
/// inexistente (<c>NotFound</c>), dono inativo (<c>FailedPrecondition</c>) e
/// Identity indisponível (<c>Unavailable</c>). <b>Requer Docker.</b>
/// </summary>
[Collection("Postgres")]
public class CreateTaskPostgresRejectionTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;

    public CreateTaskPostgresRejectionTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("Category", "Docker")]
    public async Task CreateTask_DonoInexistenteOuInativo_NenhumaLinhaGravadaNoPostgresReal()
    {
        await _fixture.EnsureStartedAsync();

        await using var identityFactory = new WebApplicationFactory<IdentityProgram>();
        await using var factory = CreateFactoryRoteadoParaIdentityReal(identityFactory);
        await MigrateAsync(factory);
        using var client = new TasksGrpcTestClient(factory.Server.CreateHandler(), factory.Server.BaseAddress);

        var usuarioInexistente = Guid.NewGuid();
        var excecaoInexistente = await CallAndCaptureFailureAsync(client, usuarioInexistente, "Dono inexistente (Postgres real)");
        excecaoInexistente.StatusCode.Should().Be(StatusCode.NotFound);

        var excecaoInativo = await CallAndCaptureFailureAsync(client, InMemoryUserLookup.InactiveUserId, "Dono inativo (Postgres real)");
        excecaoInativo.StatusCode.Should().Be(StatusCode.FailedPrecondition);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        (await context.Tasks.AnyAsync(task =>
                task.OwnerId == usuarioInexistente || task.OwnerId == InMemoryUserLookup.InactiveUserId))
            .Should().BeFalse("nenhuma das duas rejeições deve gravar linha, nem no Postgres real");
    }

    [Fact]
    [Trait("Category", "Docker")]
    public async Task CreateTask_IdentityIndisponivel_NenhumaLinhaGravadaNoPostgresReal()
    {
        await _fixture.EnsureStartedAsync();

        var enderecoMorto = TasksApiFactory.GetUnreachableAddress();
        await using var factory = CreateFactoryComIdentityMorto(enderecoMorto);
        await MigrateAsync(factory);
        using var client = new TasksGrpcTestClient(factory.Server.CreateHandler(), factory.Server.BaseAddress);

        var owner = Guid.NewGuid();
        var exception = await CallAndCaptureFailureAsync(client, owner, "Identity indisponível (Postgres real)");
        exception.StatusCode.Should().Be(StatusCode.Unavailable);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        (await context.Tasks.AnyAsync(task => task.OwnerId == owner)).Should().BeFalse(
            "a indisponibilidade do Identity não deve gravar linha, nem no Postgres real (D-28)");
    }

    private WebApplicationFactory<Program> CreateFactoryRoteadoParaIdentityReal(WebApplicationFactory<IdentityProgram> identityFactory)
    {
        var identityAddress = new Uri("http://identity.test");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            ConfigureCommon(builder, identityAddress);

            builder.ConfigureServices(services =>
                services.AddGrpcClient<IdentityService.IdentityServiceClient>(options => options.Address = identityAddress)
                    .ConfigurePrimaryHttpMessageHandler(() => identityFactory.Server.CreateHandler()));
        });
    }

    private WebApplicationFactory<Program> CreateFactoryComIdentityMorto(Uri enderecoMorto) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => ConfigureCommon(builder, enderecoMorto));

    private void ConfigureCommon(IWebHostBuilder builder, Uri identityAddress) =>
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{TodoList.Tasks.Infrastructure.Persistence.ServiceCollectionExtensions.ConnectionStringName}"] = _fixture.ConnectionString,
                ["Identity:GrpcAddress"] = identityAddress.ToString(),
                ["Identity:GrpcTimeoutSeconds"] = "2",
                ["Service:DisplayName"] = "Tasks Service (teste Postgres real)",
            });
        });

    private async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        // A FK cruzada de schema (tasks.tasks.owner_id → identity.users(id),
        // D-27) exige que o schema "identity" já exista antes da migration
        // do Tasks rodar — por isso o Identity migra primeiro, contra o
        // MESMO Postgres do container.
        await using (var identityOptions = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(
                    _fixture.ConnectionString,
                    npgsql => npgsql.MigrationsHistoryTable(IdentityDbContext.MigrationsHistoryTableName, IdentityDbContext.Schema))
                .UseSnakeCaseNamingConvention()
                .Options))
        {
            await identityOptions.Database.MigrateAsync();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        await context.Database.MigrateAsync();
    }

    private static async Task<RpcException> CallAndCaptureFailureAsync(TasksGrpcTestClient client, Guid userId, string title)
    {
        try
        {
            await client.CreateTaskAsync(new ProtoCreateTaskRequest { Title = title }, TasksGrpcTestClient.OwnerHeaders(userId.ToString()));
            throw new InvalidOperationException("Esperava RpcException de falha, mas a chamada teve sucesso.");
        }
        catch (RpcException exception)
        {
            return exception;
        }
    }
}
