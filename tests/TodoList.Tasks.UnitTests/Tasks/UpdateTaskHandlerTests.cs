using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TodoList.Tasks.Application.Errors;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Application.Security;
using TodoList.Tasks.Application.Tasks;
using TodoList.Tasks.Domain.Tasks;
using Xunit;

namespace TodoList.Tasks.UnitTests.Tasks;

/// <summary>
/// <see cref="UpdateTaskHandler"/> (BE-19) com <see cref="ITodoTaskRepository"/>
/// substituído (NSubstitute) — CA-03, CA-08, CA-10, CA-14 (as que cabem à
/// camada de Application; o restante dos critérios de aceite de BE-19 é
/// integração, contra um banco de verdade).
/// </summary>
public class UpdateTaskHandlerTests
{
    private static readonly Guid _ownerId = Guid.NewGuid();
    private static readonly Guid _taskId = Guid.NewGuid();

    private readonly ITodoTaskRepository _repository = Substitute.For<ITodoTaskRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IClientDate _clientDate = Substitute.For<IClientDate>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    public UpdateTaskHandlerTests()
    {
        _currentUser.Id.Returns(_ownerId);
        _clientDate.Today.Returns(new DateOnly(2026, 1, 1));
    }

    [Fact] // CA-11/CA-12 de BE-19 — null do repositório (inexistente, alheia ou removida) vira sempre o mesmo NotFound
    public async Task HandleAsync_TarefaNaoEncontrada_RetornaTaskErrorsNotFoundSemPersistir()
    {
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns((TodoTask?)null);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(
            new UpdateTaskRequest(_taskId, "Novo título", null, TaskPriority.Medium, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TaskErrors.NotFound);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // CA-03 — updatedAt muda; createdAt não muda (verificado pelo próprio Domain, aqui só confirmamos que persiste)
    public async Task HandleAsync_RequestValido_AtualizaOsCamposEPersiste()
    {
        var task = CriarTarefaExistente();
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns(task);
        var handler = CreateHandler();
        var createdAtOriginal = task.CreatedAt;

        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        var result = await handler.HandleAsync(
            new UpdateTaskRequest(_taskId, "Título editado", "Nova descrição", TaskPriority.High, new DateOnly(2027, 1, 1)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("Título editado");
        result.Value.Description.Should().Be("Nova descrição");
        result.Value.Priority.Should().Be(TaskPriority.High);
        result.Value.DueDate.Should().Be(new DateOnly(2027, 1, 1));
        task.CreatedAt.Should().Be(createdAtOriginal);
        task.UpdatedAt.Should().Be(_timeProvider.GetUtcNow().UtcDateTime);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // CA-04/CA-05 — description e dueDate nulos limpam os campos (semântica de substituição)
    public async Task HandleAsync_ComDescriptionEDueDateNulos_LimpaOsCampos()
    {
        var task = CriarTarefaExistente();
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns(task);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(
            new UpdateTaskRequest(_taskId, "Só título", null, TaskPriority.Medium, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Description.Should().BeNull();
        result.Value.DueDate.Should().BeNull();
    }

    [Fact] // CA-10 — tarefa concluída pode ser editada e permanece Completed, sem alterar completedAt
    public async Task HandleAsync_TarefaConcluida_PermaneceCompletedComCompletedAtInalterado()
    {
        var task = CriarTarefaExistente();
        task.Complete(_timeProvider);
        var completedAtOriginal = task.CompletedAt;
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns(task);
        var handler = CreateHandler();

        _timeProvider.Advance(TimeSpan.FromMinutes(1));

        var result = await handler.HandleAsync(
            new UpdateTaskRequest(_taskId, "Editada mesmo concluída", null, TaskPriority.Low, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(TodoTaskStatus.Completed);
        task.CompletedAt.Should().Be(completedAtOriginal);
    }

    [Fact] // CA-14 — falha de validação (título vazio) não persiste
    public async Task HandleAsync_TituloInvalido_FalhaSemPersistir()
    {
        var task = CriarTarefaExistente();
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns(task);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(
            new UpdateTaskRequest(_taskId, "   ", null, TaskPriority.Medium, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // BE-19, "Não inclui": status e ownerId não são tocados por este handler
    public async Task HandleAsync_NaoAlteraOwnerIdNemStatus()
    {
        var task = CriarTarefaExistente();
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns(task);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(
            new UpdateTaskRequest(_taskId, "Ainda pendente", null, TaskPriority.Medium, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(TodoTaskStatus.Pending);
        task.OwnerId.Should().Be(_ownerId);
    }

    private TodoTask CriarTarefaExistente() =>
        TodoTask.Create(_ownerId, "Título original", "Descrição original", TaskPriority.Low, null, _timeProvider).Value;

    private UpdateTaskHandler CreateHandler() => new(_repository, _unitOfWork, _currentUser, _clientDate, _timeProvider);
}
