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
/// <see cref="DeleteTaskHandler"/> (BE-21) com <see cref="ITodoTaskRepository"/>
/// substituído — CA-03 (updatedAt muda) e CA-08 (idempotência: tarefa já
/// removida vira NotFound, porque <c>GetOwnedTaskAsync</c> já a filtra fora).
/// </summary>
public class DeleteTaskHandlerTests
{
    private static readonly Guid _ownerId = Guid.NewGuid();
    private static readonly Guid _taskId = Guid.NewGuid();

    private readonly ITodoTaskRepository _repository = Substitute.For<ITodoTaskRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    public DeleteTaskHandlerTests()
    {
        _currentUser.Id.Returns(_ownerId);
    }

    [Fact] // CA-08 — tarefa já removida (ou inexistente/alheia): o mesmo NotFound, GetOwnedTaskAsync já a exclui
    public async Task HandleAsync_TarefaNaoEncontrada_RetornaNotFoundSemPersistir()
    {
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns((TodoTask?)null);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(_taskId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TaskErrors.NotFound);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // CA-03 — updatedAt é atualizado pela remoção; DeletedAt preenchido (soft delete, D-07)
    public async Task HandleAsync_TarefaPropria_MarcaDeletedAtEAtualizaUpdatedAt()
    {
        var task = TodoTask.Create(_ownerId, "Tarefa", null, null, null, _timeProvider).Value;
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns(task);
        var handler = CreateHandler();

        _timeProvider.Advance(TimeSpan.FromMinutes(2));

        var result = await handler.HandleAsync(_taskId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.DeletedAt.Should().Be(_timeProvider.GetUtcNow().UtcDateTime);
        task.UpdatedAt.Should().Be(_timeProvider.GetUtcNow().UtcDateTime);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // Tarefa concluída também pode ser removida (CA-07 de BE-21)
    public async Task HandleAsync_TarefaConcluida_TambemPodeSerRemovida()
    {
        var task = TodoTask.Create(_ownerId, "Tarefa", null, null, null, _timeProvider).Value;
        task.Complete(_timeProvider);
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns(task);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(_taskId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.DeletedAt.Should().NotBeNull();
    }

    private DeleteTaskHandler CreateHandler() => new(_repository, _unitOfWork, _currentUser, _timeProvider);
}
