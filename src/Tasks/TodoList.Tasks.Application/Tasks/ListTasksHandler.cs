using Microsoft.Extensions.Options;
using TodoList.SharedKernel;
using TodoList.Tasks.Application.Errors;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Application.Security;

namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Caso de uso de listagem de tarefas (BE-22) — RN-LIST-01 a RN-LIST-07.
/// Passos:
/// <list type="number">
/// <item>resolve <c>page</c>/<c>pageSize</c> efetivos, aplicando o padrão
/// (<see cref="PagingOptions.DefaultPageSize"/>) quando o request manda
/// <c>0</c> (CA-06 de BE-41/CA-24 de BE-22);</item>
/// <item>valida os valores efetivos contra os limites de <see cref="PagingOptions"/>
/// (CA-29/CA-30) — segunda linha de defesa: o Gateway já valida isso na
/// borda, mas este handler não confia só nisso;</item>
/// <item>lê <see cref="IClientDate.Today"/> <b>uma única vez</b> e usa o
/// mesmo valor tanto no <see cref="TaskListFilter"/> (filtro <c>overdue</c>)
/// quanto na projeção de cada item (<c>isOverdue</c>) — é assim que CA-33c
/// é garantido: não há como os dois divergirem se os dois lêem a mesma
/// variável local, em vez de cada um chamar <see cref="IClientDate.Today"/>
/// de novo;</item>
/// <item>trata <c>search</c> vazio/só-espaços como ausente (CA-17), com
/// <c>Trim()</c> do valor efetivo;</item>
/// <item>consulta <see cref="ITodoTaskRepository.ListByOwnerAsync"/>, que
/// filtra por <see cref="ICurrentUser.Id"/> e aplica todo o resto (filtros,
/// busca, ordenação, <c>Skip</c>/<c>Take</c>) no banco (CA-02, CA-03, CA-32);</item>
/// <item>mapeia cada item com <see cref="TaskResponseMapper"/>, usando o
/// mesmo <c>today</c> do passo 3 (CA-10/CA-33b/CA-33c).</item>
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

        // CA-33b/CA-33c: uma única leitura de "hoje", reaproveitada pelo
        // filtro (abaixo, via TaskListFilter) e pela projeção (ToResponse) —
        // se os dois lessem IClientDate.Today separadamente, um valor
        // poderia mudar entre as duas leituras (ex.: uma chamada exatamente
        // na virada da meia-noite) e produzir a divergência que CA-33c
        // proíbe.
        var today = _clientDate.Today;

        // CA-17: search vazio ou só espaços é tratado como ausente.
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();

        var filter = new TaskListFilter(
            request.Status,
            request.Priorities ?? [],
            request.Overdue,
            search,
            today);

        var (tasks, totalCount) = await _repository.ListByOwnerAsync(ownerId, page, pageSize, filter, cancellationToken);

        var items = tasks.Select(task => task.ToResponse(today)).ToList();

        return Result.Success(new ListTasksResponse(items, page, pageSize, totalCount));
    }
}
