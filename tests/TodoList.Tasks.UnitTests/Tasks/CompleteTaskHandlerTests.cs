using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TodoList.SharedKernel;
using TodoList.Tasks.Application.Errors;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Application.Security;
using TodoList.Tasks.Domain.Tasks;
using Xunit;

namespace TodoList.Tasks.UnitTests.Tasks;

/// <summary>
/// <see cref="Application.Tasks.CompleteTaskHandler"/> (BE-20) com
/// <see cref="ITodoTaskRepository"/> substituído — CA-02 a CA-05, CA-12
/// (com <see cref="FakeTimeProvider"/> determinístico).
/// </summary>
public class CompleteTaskHandlerTests
{
    private static readonly Guid _ownerId = Guid.NewGuid();
    private static readonly Guid _taskId = Guid.NewGuid();

    private readonly ITodoTaskRepository _repository = Substitute.For<ITodoTaskRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IClientDate _clientDate = Substitute.For<IClientDate>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    public CompleteTaskHandlerTests()
    {
        _currentUser.Id.Returns(_ownerId);
        _clientDate.Today.Returns(new DateOnly(2026, 1, 1));
    }

    [Fact] // Tarefa inexistente/alheia/removida: mesmo NotFound
    public async Task HandleAsync_TarefaNaoEncontrada_RetornaNotFound()
    {
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns((TodoTask?)null);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(_taskId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TaskErrors.NotFound);
    }

    [Fact] // CA-02, CA-03 — completedAt preenchido em UTC, updatedAt muda
    public async Task HandleAsync_TarefaPending_PreencheCompletedAtEAtualizaUpdatedAt()
    {
        var task = TodoTask.Create(_ownerId, "Tarefa", null, null, null, _timeProvider).Value;
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns(task);
        var handler = CreateHandler();

        _timeProvider.Advance(TimeSpan.FromMinutes(3));

        var result = await handler.HandleAsync(_taskId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(TodoTaskStatus.Completed);
        result.Value.CompletedAt.Should().Be(_timeProvider.GetUtcNow().UtcDateTime);
        result.Value.UpdatedAt.Should().Be(_timeProvider.GetUtcNow().UtcDateTime);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // CA-04, CA-05 — concluir de novo é conflito, sem persistir nem alterar completedAt/updatedAt
    public async Task HandleAsync_TarefaJaConcluida_RetornaAlreadyCompletedSemAlterarNada()
    {
        var task = TodoTask.Create(_ownerId, "Tarefa", null, null, null, _timeProvider).Value;
        task.Complete(_timeProvider);
        var completedAtOriginal = task.CompletedAt;
        var updatedAtOriginal = task.UpdatedAt;
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns(task);
        var handler = CreateHandler();

        _timeProvider.Advance(TimeSpan.FromMinutes(10));

        var result = await handler.HandleAsync(_taskId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TodoTaskErrors.AlreadyCompleted);
        task.CompletedAt.Should().Be(completedAtOriginal);
        task.UpdatedAt.Should().Be(updatedAtOriginal);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // CA-12 — ciclo complete -> reopen -> complete: segundo completedAt é posterior ao primeiro
    public async Task HandleAsync_CicloCompleteReopenComplete_SegundoCompletedAtEhPosterior()
    {
        var task = TodoTask.Create(_ownerId, "Tarefa", null, null, null, _timeProvider).Value;
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns(task);
        var handler = CreateHandler();

        await handler.HandleAsync(_taskId, CancellationToken.None);
        var primeiroCompletedAt = task.CompletedAt!.Value;

        task.Reopen(_timeProvider);
        _timeProvider.Advance(TimeSpan.FromMinutes(1));

        var result = await handler.HandleAsync(_taskId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CompletedAt!.Value.Should().BeAfter(primeiroCompletedAt);
    }

    private Application.Tasks.CompleteTaskHandler CreateHandler() =>
        new(_repository, _unitOfWork, _currentUser, _clientDate, _timeProvider);
}
