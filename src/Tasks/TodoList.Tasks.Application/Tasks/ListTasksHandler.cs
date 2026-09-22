using Microsoft.Extensions.Options;
using TodoList.SharedKernel;
using TodoList.Tasks.Application.Errors;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Application.Security;

namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Caso de uso de listagem de tarefas (BE-41, recorte estrito de BE-22) —
/// RN-LIST-01, RN-LIST-06 (parcial), RN-LIST-07. Passos:
/// <list type="number">
/// <item>resolve <c>page</c>/<c>pageSize</c> efetivos, aplicando o padrão
/// (<see cref="PagingOptions.DefaultPageSize"/>) quando o request manda
/// <c>0</c> (CA-06);</item>
/// <item>valida os valores efetivos contra os limites de <see cref="PagingOptions"/>
/// (CA-07) — segunda linha de defesa: o Gateway já valida isso na borda,
/// mas este handler não confia só nisso;</item>
/// <item>consulta <see cref="ITodoTaskRepository.ListByOwnerAsync"/>, que
/// filtra por <see cref="ICurrentUser.Id"/> e aplica <c>Skip</c>/<c>Take</c>
/// no banco (CA-02, CA-03, CA-11);</item>
/// <item>mapeia cada item com <see cref="TaskResponseMapper"/>, usando
/// <see cref="IClientDate.Today"/> para <c>isOverdue</c> (CA-10, D-18).</item>
/// </list>
/// </summary>
public sealed class ListTasksHandler
{
    private readonly ITodoTaskRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly IClientDate _clientDate;
    private readonly IOptions<PagingOptions> _pagingOptions;

    public ListTasksHandler(
        ITodoTaskRepository repository,
        ICurrentUser currentUser,
        IClientDate clientDate,
        IOptions<PagingOptions> pagingOptions)
    {
        _repository = repository;
        _currentUser = currentUser;
        _clientDate = clientDate;
        _pagingOptions = pagingOptions;
    }

    public async Task<Result<ListTasksResponse>> HandleAsync(ListTasksRequest request, CancellationToken cancellationToken)
    {
        var options = _pagingOptions.Value;

        // CA-06: 0 significa "não informado" — aplica o padrão. Um valor
        // negativo não é normalizado: cai direto na validação abaixo (CA-07).
        var page = request.Page == 0 ? 1 : request.Page;
        var pageSize = request.PageSize == 0 ? options.DefaultPageSize : request.PageSize;

        if (page < 1)
        {
            return Result.Failure<ListTasksResponse>(TaskErrors.InvalidPage);
        }

        if (pageSize < 1 || pageSize > options.MaxPageSize)
        {
            return Result.Failure<ListTasksResponse>(TaskErrors.InvalidPageSize(options.MaxPageSize));
        }

        var ownerId = _currentUser.Id;
        var today = _clientDate.Today;

        var (tasks, totalCount) = await _repository.ListByOwnerAsync(ownerId, page, pageSize, cancellationToken);

        var items = tasks.Select(task => task.ToResponse(today)).ToList();

        return Result.Success(new ListTasksResponse(items, page, pageSize, totalCount));
    }
}
