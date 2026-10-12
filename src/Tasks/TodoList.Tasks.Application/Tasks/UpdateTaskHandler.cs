using TodoList.SharedKernel;
using TodoList.Tasks.Application.Errors;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Application.Security;

namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Caso de uso de edição de tarefa (BE-19) — RN-TASK-11, RN-TASK-14,
/// RN-AUTZ-02, RN-AUTZ-03. Passos:
/// <list type="number">
/// <item>resolve a tarefa exclusivamente por
/// <see cref="ITodoTaskRepository.GetOwnedTaskAsync"/> (BE-18) — inexistente,
/// de outro dono ou removida chegam aqui como o mesmo <c>null</c> e viram o
/// mesmo <see cref="TaskErrors.NotFound"/> (CA-11, CA-12);</item>
/// <item>chama <see cref="Domain.Tasks.TodoTask.UpdateDetails"/> — a mesma
/// validação de título/descrição da criação (BE-17), sem duplicação, porque
/// é o próprio Domain quem valida; <c>status</c>, <c>ownerId</c>,
/// <c>createdAt</c> e <c>completedAt</c> não são tocados por este método
/// (CA-08, CA-09, CA-10);</item>
/// <item>persiste.</item>
/// </list>
/// </summary>
public sealed class UpdateTaskHandler
{
    private readonly ITodoTaskRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IClientDate _clientDate;
    private readonly TimeProvider _timeProvider;

    public UpdateTaskHandler(
        ITodoTaskRepository repository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IClientDate clientDate,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clientDate = clientDate;
        _timeProvider = timeProvider;
    }

    public async Task<Result<TaskResponse>> HandleAsync(UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        var task = await _repository.GetOwnedTaskAsync(_currentUser.Id, request.TaskId, cancellationToken);

        if (task is null)
        {
            return Result.Failure<TaskResponse>(TaskErrors.NotFound);
        }

        var updateResult = task.UpdateDetails(
            request.Title, request.Description, request.Priority, request.DueDate, _timeProvider);

        if (updateResult.IsFailure)
        {
            return Result.Failure<TaskResponse>(updateResult.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(task.ToResponse(_clientDate.Today));
    }
}
