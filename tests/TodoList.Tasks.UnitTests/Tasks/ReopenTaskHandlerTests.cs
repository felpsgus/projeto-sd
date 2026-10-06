using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TodoList.SharedKernel;
using TodoList.Tasks.Application.Errors;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Application.Security;
using TodoList.Tasks.Application.Tasks;
using TodoList.Tasks.Domain.Tasks;
using Xunit;

namespace TodoList.Tasks.UnitTests.Tasks;

/// <summary>
/// <see cref="Application.Tasks.ReopenTaskHandler"/> (BE-20) com
/// <see cref="ITodoTaskRepository"/> substituído — CA-08 a CA-10, mais o
/// limite de tarefas ativas (RN-TASK-15) na reabertura, decisão de
/// 23/09/2026.
/// </summary>
public class ReopenTaskHandlerTests
{
    private static readonly Guid _ownerId = Guid.NewGuid();
    private static readonly Guid _taskId = Guid.NewGuid();

    private readonly ITodoTaskRepository _repository = Substitute.For<ITodoTaskRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IClientDate _clientDate = Substitute.For<IClientDate>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    public ReopenTaskHandlerTests()
    {
        _currentUser.Id.Returns(_ownerId);
        _clientDate.Today.Returns(new DateOnly(2026, 1, 1));
        _repository.CountActiveByOwnerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(0);
    }

    [Fact]
    public async Task HandleAsync_TarefaNaoEncontrada_RetornaNotFound()
    {
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns((TodoTask?)null);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(_taskId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TaskErrors.NotFound);
    }

    [Fact] // CA-08, CA-09 — completedAt volta a null, updatedAt muda
    public async Task HandleAsync_TarefaConcluida_LimpaCompletedAtEAtualizaUpdatedAt()
    {
        var task = TodoTask.Create(_ownerId, "Tarefa", null, null, null, _timeProvider).Value;
        task.Complete(_timeProvider);
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns(task);
        var handler = CreateHandler();

        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        var result = await handler.HandleAsync(_taskId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(TodoTaskStatus.Pending);
        result.Value.CompletedAt.Should().BeNull();
        result.Value.UpdatedAt.Should().Be(_timeProvider.GetUtcNow().UtcDateTime);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // CA-10 — reabrir tarefa Pending é conflito, sem alterar nada
    public async Task HandleAsync_TarefaJaPending_RetornaNotCompletedSemAlterarNada()
    {
        var task = TodoTask.Create(_ownerId, "Tarefa", null, null, null, _timeProvider).Value;
        var updatedAtOriginal = task.UpdatedAt;
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns(task);
        var handler = CreateHandler();

        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        var result = await handler.HandleAsync(_taskId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TodoTaskErrors.NotCompleted);
        task.UpdatedAt.Should().Be(updatedAtOriginal);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // Decisão de 23/09/2026 — reabrir com a contagem no limite falha com ActiveLimitReached, sem persistir
    public async Task HandleAsync_ContagemNoLimite_RetornaActiveLimitReachedSemPersistir()
    {
        var task = TodoTask.Create(_ownerId, "Tarefa", null, null, null, _timeProvider).Value;
        task.Complete(_timeProvider);
        var updatedAtAntes = task.UpdatedAt;
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns(task);
        _repository.CountActiveByOwnerAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(500);
        var handler = CreateHandlerComLimite(500);

        var result = await handler.HandleAsync(_taskId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TaskErrors.ActiveLimitReached(500));
        task.Status.Should().Be(TodoTaskStatus.Completed);
        task.UpdatedAt.Should().Be(updatedAtAntes);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // Decisão de 23/09/2026 — reabrir com a contagem abaixo do limite sucede normalmente
    public async Task HandleAsync_ContagemAbaixoDoLimite_Sucede()
    {
        var task = TodoTask.Create(_ownerId, "Tarefa", null, null, null, _timeProvider).Value;
        task.Complete(_timeProvider);
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns(task);
        _repository.CountActiveByOwnerAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(499);
        var handler = CreateHandlerComLimite(500);

        var result = await handler.HandleAsync(_taskId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(TodoTaskStatus.Pending);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // Decisão de 23/09/2026 — a tarefa concluída em questão não conta como ativa antes de reabrir: a contagem
           // vinda do repositório já reflete isso (composição real coberta contra banco de verdade nos testes de integração)
    public async Task HandleAsync_TarefaAReabrirNaoContaComoAtivaAntesDeReabrir_ContagemNoLimiteMenosUmSucede()
    {
        var task = TodoTask.Create(_ownerId, "Tarefa", null, null, null, _timeProvider).Value;
        task.Complete(_timeProvider);
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns(task);
        // A tarefa sendo reaberta está Completed, então não é contada pelo repositório: com limite 500 e 499 outras
        // ativas, ainda há vaga para ela virar a 500ª.
        _repository.CountActiveByOwnerAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(499);
        var handler = CreateHandlerComLimite(500);

        var result = await handler.HandleAsync(_taskId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact] // Decisão de 23/09/2026 — limite nulo desativa o bloqueio também na reabertura
    public async Task HandleAsync_ComLimiteNulo_NaoBloqueiaMesmoComContagemAlta()
    {
        var task = TodoTask.Create(_ownerId, "Tarefa", null, null, null, _timeProvider).Value;
        task.Complete(_timeProvider);
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns(task);
        _repository.CountActiveByOwnerAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(10_000);
        var handler = CreateHandlerComLimite(null);

        var result = await handler.HandleAsync(_taskId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repository.DidNotReceive().CountActiveByOwnerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private Application.Tasks.ReopenTaskHandler CreateHandler() => CreateHandlerComLimite(500);

    private Application.Tasks.ReopenTaskHandler CreateHandlerComLimite(int? maxActivePerUser) =>
        new(
            _repository,
            _unitOfWork,
            _currentUser,
            _clientDate,
            Options.Create(new TaskOptions { MaxActivePerUser = maxActivePerUser }),
            _timeProvider);
}
