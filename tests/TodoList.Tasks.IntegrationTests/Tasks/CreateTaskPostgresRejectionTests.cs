extern alias IdentityApi;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// BE-17 (CA-23), BE-28 (CA-04, CA-06, CA-09) — prova, contra
/// <b>Postgres real</b> (Testcontainers, não o SQLite in-memory do resto
/// desta pasta), que os três desfechos de rejeição de <c>POST /api/tasks</c>
/// não gravam nenhuma linha em <c>tasks.tasks</c>: dono inexistente (404),
/// dono inativo (409) e Identity indisponível (503). <b>Requer Docker.</b>
/// Ver <see cref="PostgresContainerFixture"/> e o README para excluir a
/// categoria num ambiente sem Docker
/// (<c>dotnet test --filter "Category!=Docker"</c>).
/// </summary>
[Collection("Postgres")]
public class CreateTaskPostgresRejectionTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;

    public CreateTaskPostgresRejectionTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    // Chamado só de dentro do corpo de um teste (ver o comentário em
    // PostgresContainerFixture): não fala com o daemon Docker quando a
    // categoria está filtrada fora.
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("Category", "Docker")]
    public async Task PostTasks_DonoInexistenteOuInativo_NenhumaLinhaGravadaNoPostgresReal()
    {
        await _fixture.EnsureStartedAsync();

        await using var identityFactory = new WebApplicationFactory<IdentityProgram>();
        await using var factory = CreateFactoryRoteadoParaIdentityReal(identityFactory);
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var usuarioInexistente = Guid.NewGuid();
        var respostaInexistente = await PostAsync(client, usuarioInexistente, "Dono inexistente (Postgres real)");
        respostaInexistente.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var respostaInativo = await PostAsync(client, InMemoryUserLookup.InactiveUserId, "Dono inativo (Postgres real)");
        respostaInativo.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        (await context.Tasks.AnyAsync(task =>
                task.OwnerId == usuarioInexistente || task.OwnerId == InMemoryUserLookup.InactiveUserId))
            .Should().BeFalse("nenhuma das duas rejeições deve gravar linha, nem no Postgres real");
    }

    [Fact]
    [Trait("Category", "Docker")]
    public async Task PostTasks_IdentityIndisponivel_NenhumaLinhaGravadaNoPostgresReal()
    {
        await _fixture.EnsureStartedAsync();

        var enderecoMorto = TasksApiFactory.GetUnreachableAddress();
        await using var factory = CreateFactoryComIdentityMorto(enderecoMorto);
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var owner = Guid.NewGuid();
        var resposta = await PostAsync(client, owner, "Identity indisponível (Postgres real)");
        resposta.StatusCode.Should().Be((HttpStatusCode)StatusCodes.Status503ServiceUnavailable);

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
                ["Tasks:AllowAnonymousCreate"] = "true",
                ["Service:DisplayName"] = "Tasks Service (teste Postgres real)",
            });
        });

    private async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        // A FK cruzada de schema (tasks.tasks.owner_id → identity.users(id),
        // D-27) exige que o schema "identity" já exista antes da migration
        // do Tasks rodar — por isso o Identity migra primeiro, contra o
        // MESMO Postgres do container. Migrations reais (não EnsureCreated):
        // é o mesmo schema versionado que roda em produção — chamado aqui,
        // no arranjo do teste, nunca em código de produção (BE-02).
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

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Guid userId, string title)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tasks")
        {
            Content = JsonContent.Create(new { title }),
        };
        request.Headers.Add("X-User-Id", userId.ToString());

        return await client.SendAsync(request);
    }
}
