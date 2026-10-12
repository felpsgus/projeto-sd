using FluentAssertions;
using NSubstitute;
using TodoList.Tasks.Application.Errors;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Application.Security;
using TodoList.Tasks.Application.Tasks;
using TodoList.Tasks.Domain.Tasks;
using Xunit;

namespace TodoList.Tasks.UnitTests.Tasks;

/// <summary>
/// <see cref="GetTaskHandler"/> (BE-41, recorte de BE-18) com
/// <see cref="ITodoTaskRepository"/> substituído (NSubstitute) — prova que o
/// handler consulta exclusivamente via
/// <see cref="ITodoTaskRepository.GetOwnedTaskAsync"/> (o único método de
/// resolução por id) e que <c>null</c> vira <see cref="TaskErrors.NotFound"/>,
/// qualquer que seja a causa.
/// </summary>
public class GetTaskHandlerTests
{
    private static readonly Guid _ownerId = Guid.NewGuid();
    private static readonly Guid _taskId = Guid.NewGuid();

    private readonly ITodoTaskRepository _repository = Substitute.For<ITodoTaskRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IClientDate _clientDate = Substitute.For<IClientDate>();

    public GetTaskHandlerTests()
    {
        _currentUser.Id.Returns(_ownerId);
        _clientDate.Today.Returns(new DateOnly(2026, 1, 1));
    }

    [Fact] // CA-12 — tarefa própria: sucesso com isOverdue calculado pelo IClientDate
    public async Task HandleAsync_TarefaPropriaEncontrada_RetornaSucessoComIsOverdueCalculado()
    {
        var task = TodoTask.Create(_ownerId, "Tarefa", null, null, new DateOnly(2025, 12, 31), TimeProvider.System).Value;
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns(task);

        var handler = CreateHandler();

        var result = await handler.HandleAsync(_taskId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsOverdue.Should().BeTrue();
    }

    [Fact] // CA-13/CA-14/CA-16 — null do repositório (inexistente, de outro dono ou removida) vira sempre o mesmo NotFound
    public async Task HandleAsync_RepositorioDevolveNull_RetornaTaskErrorsNotFound()
    {
        _repository.GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>()).Returns((TodoTask?)null);

        var handler = CreateHandler();

        var result = await handler.HandleAsync(_taskId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TaskErrors.NotFound);
    }

    [Fact] // A consulta usa sempre o dono corrente, nunca um id de dono arbitrário
    public async Task HandleAsync_ConsultaORepositorioComODonoCorrenteEOIdPedido()
    {
        _repository.GetOwnedTaskAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((TodoTask?)null);
        var handler = CreateHandler();

        await handler.HandleAsync(_taskId, CancellationToken.None);

        await _repository.Received(1).GetOwnedTaskAsync(_ownerId, _taskId, Arg.Any<CancellationToken>());
    }

    private GetTaskHandler CreateHandler() => new(_repository, _currentUser, _clientDate);
}
