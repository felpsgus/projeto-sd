using TodoList.SharedKernel;
using TodoList.Tasks.Application.Errors;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Application.Security;

namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Caso de uso de consulta de tarefa por id (BE-41, recorte estrito de
/// BE-18) — RN-AUTZ-01 a RN-AUTZ-03. Consome exclusivamente
/// <see cref="ITodoTaskRepository.GetOwnedTaskAsync"/>, o único método de
/// resolução por id que existe na Application (BE-18, nota técnica): tarefa
/// inexistente, de outro dono ou removida chegam aqui como o mesmo
/// <c>null</c>, e viram o mesmo <see cref="TaskErrors.NotFound"/> — nenhum
/// detalhe que distinga as três causas atravessa este handler (RN-AUTZ-03).
/// </summary>
public sealed class GetTaskHandler
{
    private readonly ITodoTaskRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly IClientDate _clientDate;

    public GetTaskHandler(ITodoTaskRepository repository, ICurrentUser currentUser, IClientDate clientDate)
    {
        _repository = repository;
        _currentUser = currentUser;
        _clientDate = clientDate;
    }

    public async Task<Result<TaskResponse>> HandleAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await _repository.GetOwnedTaskAsync(_currentUser.Id, taskId, cancellationToken);

        if (task is null)
        {
            return Result.Failure<TaskResponse>(TaskErrors.NotFound);
        }

        return Result.Success(task.ToResponse(_clientDate.Today));
    }
}
