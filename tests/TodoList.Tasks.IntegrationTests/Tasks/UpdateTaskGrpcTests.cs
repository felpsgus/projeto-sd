using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TodoList.Contracts.Tasks.V1;
using Xunit;
using DomainTaskPriority = TodoList.Tasks.Domain.Tasks.TaskPriority;
using ProtoTaskStatus = TodoList.Contracts.Tasks.V1.TaskStatus;
using TodoTask = TodoList.Tasks.Domain.Tasks.TodoTask;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// <c>UpdateTask</c> gRPC (BE-19) — CA-01 a CA-14, exceto o teste transversal
/// de autorização (CA-11, coberto pelo equivalente de BE-18 no Gateway). Não
/// fala com o Identity, mesma observação de <see cref="GetTaskGrpcTests"/>.
/// </summary>
public sealed class UpdateTaskGrpcTests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _connection = null!;
    private FakeTimeProvider _timeProvider = null!;
    private TasksApiFactory _factory = null!;
    private TasksGrpcTestClient _client = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero));
        _factory = new TasksApiFactory(_connection, _timeProvider, identityFactory: null, new Uri("http://identity.test"));

        await _factory.EnsureDatabaseCreatedAsync();
        _client = new TasksGrpcTestClient(_factory.Server.CreateHandler(), _factory.Server.BaseAddress);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-01, CA-02 — PUT válido: 200 (OK) com os novos valores
    public async Task UpdateTask_RequestValido_AtualizaOsQuatroCamposEditaveis()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Original", "Descrição original", TaskPriority.Low, new DateOnly(2026, 1, 1));

        var reply = await UpdateAsync(owner, id, new UpdateTaskRequest
        {
            Id = id.ToString(),
            Title = "Editado",
            Description = "Nova descrição",
            Priority = TaskPriority.High,
            DueDate = "2027-01-01",
        });

        reply.Title.Should().Be("Editado");
        reply.Description.Should().Be("Nova descrição");
        reply.Priority.Should().Be(TaskPriority.High);
        reply.DueDate.Should().Be("2027-01-01");
    }

    [Fact] // CA-03 — updatedAt muda; createdAt não muda
    public async Task UpdateTask_RequestValido_AtualizaUpdatedAtMasNaoCreatedAt()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Original", null, TaskPriority.Medium, null);
        var createdAtOriginal = await ReadCreatedAtAsync(id);

        _timeProvider.Advance(TimeSpan.FromMinutes(10));

        var reply = await UpdateAsync(owner, id, new UpdateTaskRequest { Id = id.ToString(), Title = "Editado", Priority = TaskPriority.Medium });

        reply.UpdatedAt.ToDateTime().Should().Be(_timeProvider.GetUtcNow().UtcDateTime);
        var createdAtDepois = await ReadCreatedAtAsync(id);
        createdAtDepois.Should().Be(createdAtOriginal);
    }

    [Fact] // CA-04 — description e dueDate ausentes (null) limpam os campos
    public async Task UpdateTask_ComDescriptionEDueDateAusentes_LimpaOsCampos()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Original", "Tinha descrição", TaskPriority.Medium, new DateOnly(2026, 5, 1));

        var reply = await UpdateAsync(owner, id, new UpdateTaskRequest { Id = id.ToString(), Title = "Editado" });

        reply.HasDescription.Should().BeFalse();
        reply.HasDueDate.Should().BeFalse();
    }

    [Fact] // CA-05 — priority ausente (UNSPECIFIED) assume o padrão de substituição Medium
    public async Task UpdateTask_ComPriorityAusente_AssumeMedium()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Original", null, TaskPriority.High, null);

        var reply = await UpdateAsync(owner, id, new UpdateTaskRequest { Id = id.ToString(), Title = "Editado" });

        reply.Priority.Should().Be(TaskPriority.Medium);
    }

    [Theory] // CA-06 — título vazio/só-espaços/201 caracteres: erro de validação
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateTask_ComTituloInvalido_DevolveInvalidArgument(string tituloInvalido)
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Original", null, TaskPriority.Medium, null);

        var exception = await UpdateAndCaptureFailureAsync(
            owner, new UpdateTaskRequest { Id = id.ToString(), Title = tituloInvalido });

        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact] // CA-06 — descrição com 2001 caracteres: erro de validação
    public async Task UpdateTask_ComDescricaoMuitoLonga_DevolveInvalidArgument()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Original", null, TaskPriority.Medium, null);

        var exception = await UpdateAndCaptureFailureAsync(
            owner, new UpdateTaskRequest { Id = id.ToString(), Title = "Válido", Description = new string('a', 2001) });

        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact] // CA-07 — dueDate no passado é aceito e isOverdue reflete true
    public async Task UpdateTask_ComDueDateNoPassado_AceitaEMarcaIsOverdue()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Original", null, TaskPriority.Medium, null);

        var reply = await UpdateAsync(owner, id, new UpdateTaskRequest
        {
            Id = id.ToString(),
            Title = "Editado",
            DueDate = "2020-01-01",
        });

        reply.IsOverdue.Should().BeTrue();
    }

    [Fact] // CA-10 — editar tarefa concluída funciona; permanece Completed, completedAt inalterado
    public async Task UpdateTask_TarefaConcluida_PermaneceCompletedComCompletedAtInalterado()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Original", null, TaskPriority.Medium, null);
        await CompleteAsync(id);
        var completedAtOriginal = await ReadCompletedAtAsync(id);

        var reply = await UpdateAsync(owner, id, new UpdateTaskRequest { Id = id.ToString(), Title = "Editada e concluída" });

        reply.Status.Should().Be(ProtoTaskStatus.Completed);
        var completedAtDepois = await ReadCompletedAtAsync(id);
        completedAtDepois.Should().Be(completedAtOriginal);
    }

    [Fact] // CA-11 — tarefa de outro usuário: NotFound
    public async Task UpdateTask_TarefaDeOutroDono_RetornaNotFound()
    {
        var dono = Guid.NewGuid();
        var outroUsuario = Guid.NewGuid();
        var id = await SeedAsync(dono, "Não é do outro usuário", null, TaskPriority.Medium, null);

        var exception = await UpdateAndCaptureFailureAsync(
            outroUsuario, new UpdateTaskRequest { Id = id.ToString(), Title = "Tentativa" });

        exception.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact] // CA-12 — tarefa removida: NotFound
    public async Task UpdateTask_TarefaRemovida_RetornaNotFound()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Será removida", null, TaskPriority.Medium, null);
        await SoftDeleteAsync(id);

        var exception = await UpdateAndCaptureFailureAsync(
            owner, new UpdateTaskRequest { Id = id.ToString(), Title = "Tentativa" });

        exception.StatusCode.Should().Be(StatusCode.NotFound);
    }

    private async Task<Guid> SeedAsync(Guid owner, string title, string? description, TaskPriority priority, DateOnly? dueDate)
    {
        await using var context = _factory.CreateDbContext();
        var domainPriority = priority switch
        {
            TaskPriority.Low => DomainTaskPriority.Low,
            TaskPriority.High => DomainTaskPriority.High,
            _ => DomainTaskPriority.Medium,
        };
        var task = TodoTask.Create(owner, title, description, domainPriority, dueDate, _timeProvider).Value;
        context.Tasks.Add(task);
        await context.SaveChangesAsync();

        return task.Id;
    }

    private async Task SoftDeleteAsync(Guid taskId)
    {
        await using var context = _factory.CreateDbContext();
        var task = await context.Tasks.SingleAsync(t => t.Id == taskId);
        task.SoftDelete(_timeProvider);
        await context.SaveChangesAsync();
    }

    private async Task CompleteAsync(Guid taskId)
    {
        await using var context = _factory.CreateDbContext();
        var task = await context.Tasks.SingleAsync(t => t.Id == taskId);
        task.Complete(_timeProvider);
        await context.SaveChangesAsync();
    }

    private async Task<DateTime> ReadCreatedAtAsync(Guid taskId)
    {
        await using var context = _factory.CreateDbContext();
        return (await context.Tasks.SingleAsync(t => t.Id == taskId)).CreatedAt;
    }

    private async Task<DateTime?> ReadCompletedAtAsync(Guid taskId)
    {
        await using var context = _factory.CreateDbContext();
        return (await context.Tasks.SingleAsync(t => t.Id == taskId)).CompletedAt;
    }

    private async Task<TaskReply> UpdateAsync(Guid caller, Guid taskId, UpdateTaskRequest request)
    {
        request.Id = taskId.ToString();
        var headers = TasksGrpcTestClient.OwnerHeaders(caller.ToString());
        return await _client.UpdateTaskAsync(request, headers);
    }

    private async Task<RpcException> UpdateAndCaptureFailureAsync(Guid caller, UpdateTaskRequest request)
    {
        try
        {
            var headers = TasksGrpcTestClient.OwnerHeaders(caller.ToString());
            await _client.UpdateTaskAsync(request, headers);
            throw new InvalidOperationException("Esperava RpcException de falha, mas a chamada teve sucesso.");
        }
        catch (RpcException exception)
        {
            return exception;
        }
    }
}
