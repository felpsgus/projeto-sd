extern alias TasksInfra;

using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TodoList.Contracts.Identity.V1;
using TodoList.Identity.Api.ResultMapping;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Tasks.Domain.Tasks;
using Xunit;
using TasksDbContext = TasksInfra::TodoList.Tasks.Infrastructure.Persistence.TasksDbContext;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// Exclusão de conta via gRPC (BE-16) contra Postgres real (Testcontainers) —
/// a task só se prova aqui: a cascata <c>tasks.tasks.owner_id →
/// identity.users(id) ON DELETE CASCADE</c> (D-27) exige o banco relacional
/// de verdade, inclusive para tarefas soft-deleted (CA-05, que só "desaparece"
/// da consulta comum por causa do filtro global do EF — o teste consulta com
/// <c>IgnoreQueryFilters()</c>, senão passaria mesmo com linhas remanescentes).
/// <b>Requer Docker.</b>
///
/// <para>
/// A migration do Tasks Service referencia <c>identity.users</c> (FK cruzada
/// de schema) — por isso o Identity é migrado primeiro, depois o Tasks (mesma
/// ordem documentada em <c>TasksDbContext</c>).
/// </para>
/// </summary>
[Collection("Postgres")]
public class DeleteAccountGrpcTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;
    private readonly FailingSaveChangesInterceptor _failing = new();

    public DeleteAccountGrpcTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.EnsureStartedAsync();

        await using (var identityContext = CreateIdentityProbeContext())
        {
            await identityContext.Database.EnsureDeletedAsync();
            await identityContext.Database.MigrateAsync();
        }

        // Ordem obrigatória: a FK de tasks.tasks referencia identity.users,
        // então o schema identity precisa existir antes desta migration rodar.
        await using (var tasksContext = CreateTasksProbeContext())
        {
            await tasksContext.Database.MigrateAsync();
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

    [Fact] // CA-01 a CA-05, CA-05b, CA-08, CA-13
    [Trait("Category", "Docker")]
    public async Task DeleteAccount_UsuarioComTarefasAtivasESoftDeleted_ApagaTudoEmCascataSemAfetarOutraConta()
    {
        using var client = CreateClient();
        const string password = "senha-correta-123";
        const string email = "excluir@exemplo.com";
        const string controlEmail = "controle@exemplo.com";

        var user = await client.RegisterAsync(new RegisterRequest { Email = email, Password = password, DisplayName = "" });
        var controlUser = await client.RegisterAsync(new RegisterRequest { Email = controlEmail, Password = password, DisplayName = "" });
        var userId = Guid.Parse(user.Id);
        var controlUserId = Guid.Parse(controlUser.Id);

        await using (var tasksContext = CreateTasksProbeContext())
        {
            await SeedTaskAsync(tasksContext, userId, softDeleted: false);
            await SeedTaskAsync(tasksContext, userId, softDeleted: true);
            await SeedTaskAsync(tasksContext, controlUserId, softDeleted: false);
            await SeedTaskAsync(tasksContext, controlUserId, softDeleted: true);
        }

        await client.DeleteAccountAsync(new DeleteAccountRequest { UserId = user.Id, Password = password });

        // CA-03: usuário não existe mais.
        await using (var identityContext = CreateIdentityProbeContext())
        {
            (await identityContext.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId)).Should().BeFalse();
            // Conta de controle permanece intacta (CA-08).
            (await identityContext.Users.AnyAsync(u => u.Id == controlUserId)).Should().BeTrue();
        }

        // CA-04/CA-05/CA-05b: todas as tarefas do usuário excluído sumiram,
        // inclusive as soft-deleted — via cascata do banco, sem RPC ao Tasks.
        await using (var tasksContext = CreateTasksProbeContext())
        {
            var remainingForDeletedUser = await tasksContext.Tasks.IgnoreQueryFilters().Where(t => t.OwnerId == userId).ToListAsync();
            remainingForDeletedUser.Should().BeEmpty();

            // CA-08: tarefas da conta de controle continuam intactas, ativas e soft-deleted.
            var controlTasks = await tasksContext.Tasks.IgnoreQueryFilters().Where(t => t.OwnerId == controlUserId).ToListAsync();
            controlTasks.Should().HaveCount(2);
        }

        // CA-02: login com as credenciais excluídas falha com 401/InvalidCredentials, indistinguível de e-mail inexistente.
        var loginDepois = await client.LoginAsync(new LoginRequest { Email = email, Password = password });
        loginDepois.Succeeded.Should().BeFalse();
    }

    [Fact] // CA-09: access token emitido antes da exclusão, ainda dentro da validade, passa a falhar em qualquer chamada que carregue o usuário
    [Trait("Category", "Docker")]
    public async Task DeleteAccount_TokenEmitidoAntes_PassaAResponderUnauthenticatedEmChamadaSubsequente()
    {
        using var client = CreateClient();
        const string password = "senha-correta-123";
        var user = await client.RegisterAsync(new RegisterRequest { Email = "token-antigo@exemplo.com", Password = password, DisplayName = "" });
        var login = await client.LoginAsync(new LoginRequest { Email = "token-antigo@exemplo.com", Password = password });
        login.Succeeded.Should().BeTrue();

        await client.DeleteAccountAsync(new DeleteAccountRequest { UserId = user.Id, Password = password });

        var act = async () => await client.GetProfileAsync(new GetProfileRequest { UserId = user.Id });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact] // CA-10: senha incorreta não apaga nada
    [Trait("Category", "Docker")]
    public async Task DeleteAccount_SenhaIncorreta_NaoApagaNada()
    {
        using var client = CreateClient();
        const string password = "senha-correta-123";
        var user = await client.RegisterAsync(new RegisterRequest { Email = "nao-apagar@exemplo.com", Password = password, DisplayName = "" });
        var userId = Guid.Parse(user.Id);

        await using (var tasksContext = CreateTasksProbeContext())
        {
            await SeedTaskAsync(tasksContext, userId, softDeleted: false);
        }

        var act = async () => await client.DeleteAccountAsync(new DeleteAccountRequest { UserId = user.Id, Password = "senha-errada" });

        // Decisão do tech lead (onda 2 da Fase 3): 400 (InvalidArgument), não 401 —
        // a requisição já está autenticada; o que falhou é o campo password.
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        exception.Which.Trailers.Get(ResultGrpcStatus.ErrorCodeTrailerKey)!.Value.Should().Be("auth.invalid_current_password");

        await using var identityContext = CreateIdentityProbeContext();
        (await identityContext.Users.AnyAsync(u => u.Id == userId)).Should().BeTrue();

        await using var tasksContextDepois = CreateTasksProbeContext();
        (await tasksContextDepois.Tasks.CountAsync(t => t.OwnerId == userId)).Should().Be(1);
    }

    [Fact] // CA-13: o e-mail liberado pode ser usado num novo cadastro depois da exclusão
    [Trait("Category", "Docker")]
    public async Task DeleteAccount_EmailPodeSerReutilizadoEmNovoCadastro()
    {
        using var client = CreateClient();
        const string password = "senha-correta-123";
        const string email = "reutilizavel@exemplo.com";
        var user = await client.RegisterAsync(new RegisterRequest { Email = email, Password = password, DisplayName = "" });

        await client.DeleteAccountAsync(new DeleteAccountRequest { UserId = user.Id, Password = password });

        var newUser = await client.RegisterAsync(new RegisterRequest { Email = email, Password = "outra-senha-456", DisplayName = "Novo" });

        newUser.Id.Should().NotBe(user.Id);
        newUser.Email.Should().Be(email);
    }

    [Fact] // CA-08: as sessões (refresh tokens) de outro usuário sobrevivem à exclusão
    [Trait("Category", "Docker")]
    public async Task DeleteAccount_NaoRevogaRefreshTokenDeOutroUsuario()
    {
        using var client = CreateClient();
        const string password = "senha-correta-123";
        var userA = await client.RegisterAsync(new RegisterRequest { Email = "sessao-a@exemplo.com", Password = password, DisplayName = "" });
        await client.RegisterAsync(new RegisterRequest { Email = "sessao-b@exemplo.com", Password = password, DisplayName = "" });
        var loginB = await client.LoginAsync(new LoginRequest { Email = "sessao-b@exemplo.com", Password = password });

        await client.DeleteAccountAsync(new DeleteAccountRequest { UserId = userA.Id, Password = password });

        (await client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = loginB.RefreshToken })).Succeeded.Should().BeTrue();
    }

    [Fact] // CA-11: falha na persistência -> nada é apagado (conta, sessão e tarefas)
    [Trait("Category", "Docker")]
    public async Task DeleteAccount_FalhaNaPersistencia_NaoApagaNada()
    {
        using var client = CreateClient();
        const string password = "senha-correta-123";
        var user = await client.RegisterAsync(new RegisterRequest { Email = "falha-exclusao@exemplo.com", Password = password, DisplayName = "" });
        var userId = Guid.Parse(user.Id);
        var login = await client.LoginAsync(new LoginRequest { Email = "falha-exclusao@exemplo.com", Password = password });
        await using (var tasksContext = CreateTasksProbeContext())
        {
            await SeedTaskAsync(tasksContext, userId, softDeleted: false);
        }

        _failing.Armed = true;
        var act = async () => await client.DeleteAccountAsync(new DeleteAccountRequest { UserId = user.Id, Password = password });
        await act.Should().ThrowAsync<RpcException>();
        _failing.Armed = false;

        await using var identityContext = CreateIdentityProbeContext();
        (await identityContext.Users.AnyAsync(u => u.Id == userId)).Should().BeTrue();
        await using var tasksContextDepois = CreateTasksProbeContext();
        (await tasksContextDepois.Tasks.CountAsync(t => t.OwnerId == userId)).Should().Be(1);
        (await client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = login.RefreshToken })).Succeeded.Should().BeTrue();
    }

    private static async Task SeedTaskAsync(TasksDbContext context, Guid ownerId, bool softDeleted)
    {
        var task = TodoTask.Create(
            ownerId, "Tarefa de teste", null, null, null, TimeProvider.System).Value;

        if (softDeleted)
        {
            task.SoftDelete(TimeProvider.System);
        }

        context.Tasks.Add(task);
        await context.SaveChangesAsync();
    }

    private IdentityGrpcTestClient CreateClient()
    {
        var handler = _factory.Server.CreateHandler();
        return new IdentityGrpcTestClient(handler, _factory.Server.BaseAddress);
    }

    private IdentityDbContext CreateIdentityProbeContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                _fixture.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable(IdentityDbContext.MigrationsHistoryTableName, IdentityDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new IdentityDbContext(options);
    }

    private TasksDbContext CreateTasksProbeContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseNpgsql(
                _fixture.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable(TasksDbContext.MigrationsHistoryTableName, TasksDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new TasksDbContext(options);
    }
}
