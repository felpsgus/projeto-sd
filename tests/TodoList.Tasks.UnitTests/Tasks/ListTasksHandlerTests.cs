using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using TodoList.Tasks.Application.Errors;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Application.Security;
using TodoList.Tasks.Application.Tasks;
using TodoList.Tasks.Domain.Tasks;
using Xunit;

namespace TodoList.Tasks.UnitTests.Tasks;

/// <summary>
/// <see cref="ListTasksHandler"/> (BE-22) com <see cref="ITodoTaskRepository"/>
/// substituído (NSubstitute) — a resolução de padrão (CA-06), a validação de
/// limites (CA-07/CA-29/CA-30), a passagem do dono corrente/do "hoje" do
/// cliente para o filtro e a projeção (CA-33c), e a montagem do
/// <see cref="TaskListFilter"/> a partir do request (status/prioridades/
/// atraso/busca).
/// </summary>
public class ListTasksHandlerTests
{
    private static readonly Guid _ownerId = Guid.NewGuid();

    private readonly ITodoTaskRepository _repository = Substitute.For<ITodoTaskRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IClientDate _clientDate = Substitute.For<IClientDate>();

    public ListTasksHandlerTests()
    {
        _currentUser.Id.Returns(_ownerId);
        _clientDate.Today.Returns(new DateOnly(2026, 1, 1));
        _repository.ListByOwnerAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<TaskListFilter>(), Arg.Any<CancellationToken>())
            .Returns((Array.Empty<TodoTask>(), 0));
    }

    [Fact] // CA-06 — page=0/pageSize=0 aplica page=1 e Paging:DefaultPageSize
    public async Task HandleAsync_ComPageEPageSizeZero_AplicaOPadrao()
    {
        var handler = CreateHandler(defaultPageSize: 20, maxPageSize: 100);

        var result = await handler.HandleAsync(new ListTasksRequest(0, 0), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(20);
        await _repository.Received(1).ListByOwnerAsync(_ownerId, 1, 20, Arg.Any<TaskListFilter>(), Arg.Any<CancellationToken>());
    }

    [Fact] // CA-06 — apenas pageSize=0 aplica só o pageSize padrão, preservando a page informada
    public async Task HandleAsync_ComPageInformadaEPageSizeZero_PreservaAPageEAplicaOPageSizePadrao()
    {
        var handler = CreateHandler(defaultPageSize: 20, maxPageSize: 100);

        var result = await handler.HandleAsync(new ListTasksRequest(3, 0), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Page.Should().Be(3);
        result.Value.PageSize.Should().Be(20);
    }

    [Theory] // CA-29/CA-30 — page/pageSize negativos são erro de validação, não normalizados
    [InlineData(-1, 20)]
    [InlineData(1, -1)]
    public async Task HandleAsync_ComPageOuPageSizeNegativo_RetornaFalhaDeValidacao(int page, int pageSize)
    {
        var handler = CreateHandler(defaultPageSize: 20, maxPageSize: 100);

        var result = await handler.HandleAsync(new ListTasksRequest(page, pageSize), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(TodoList.SharedKernel.ErrorType.Validation);
    }

    [Fact] // CA-29 — pageSize acima do máximo configurado é erro de validação
    public async Task HandleAsync_ComPageSizeAcimaDoMaximo_RetornaTaskErrorsInvalidPageSize()
    {
        var handler = CreateHandler(defaultPageSize: 20, maxPageSize: 100);

        var result = await handler.HandleAsync(new ListTasksRequest(1, 101), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("task.invalid_page_size");
        result.Error.Message.Should().Contain("100");
    }

    [Fact] // CA-01/CA-02 — a consulta usa o dono corrente, não outro
    public async Task HandleAsync_ConsultaORepositorioComODonoCorrente()
    {
        var handler = CreateHandler(defaultPageSize: 20, maxPageSize: 100);

        await handler.HandleAsync(new ListTasksRequest(1, 20), CancellationToken.None);

        await _repository.Received(1).ListByOwnerAsync(_ownerId, 1, 20, Arg.Any<TaskListFilter>(), Arg.Any<CancellationToken>());
    }

    [Fact] // CA-10 — isOverdue de cada item usa IClientDate.Today, não a data do servidor
    public async Task HandleAsync_MapeiaItensUsandoODiaDoClientDate()
    {
        var task = TodoTask.Create(_ownerId, "Tarefa", null, null, new DateOnly(2025, 12, 31), TimeProvider.System).Value;
        _repository.ListByOwnerAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<TaskListFilter>(), Arg.Any<CancellationToken>())
            .Returns((new[] { task }, 1));

        var handler = CreateHandler(defaultPageSize: 20, maxPageSize: 100);

        var result = await handler.HandleAsync(new ListTasksRequest(1, 20), CancellationToken.None);

        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].IsOverdue.Should().BeTrue("_clientDate.Today (2026-01-01) é posterior ao vencimento (2025-12-31)");
    }

    [Fact] // CA-08/CA-25 — total_count vem do repositório, não é recalculado a partir da página
    public async Task HandleAsync_PropagaOTotalCountDoRepositorio()
    {
        _repository.ListByOwnerAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<TaskListFilter>(), Arg.Any<CancellationToken>())
            .Returns((Array.Empty<TodoTask>(), 137));

        var handler = CreateHandler(defaultPageSize: 20, maxPageSize: 100);

        var result = await handler.HandleAsync(new ListTasksRequest(1, 20), CancellationToken.None);

        result.Value.TotalCount.Should().Be(137);
    }

    [Fact] // BE-22 — sem filtros no request, o TaskListFilter passado ao repositório não filtra por nada
    public async Task HandleAsync_SemFiltrosNoRequest_MontaFilterSemRestricoes()
    {
        var handler = CreateHandler(defaultPageSize: 20, maxPageSize: 100);

        await handler.HandleAsync(new ListTasksRequest(1, 20), CancellationToken.None);

        await _repository.Received(1).ListByOwnerAsync(
            _ownerId,
            1,
            20,
            Arg.Is<TaskListFilter>(filter =>
                filter.Status == TaskStatusFilter.All &&
                filter.Priorities.Count == 0 &&
                filter.Overdue == null &&
                filter.Search == null),
            Arg.Any<CancellationToken>());
    }

    [Fact] // BE-22, RN-LIST-02/03/04 — status, prioridades e overdue do request chegam intactos ao filtro
    public async Task HandleAsync_ComFiltrosNoRequest_RepassaTodosAoRepositorio()
    {
        var handler = CreateHandler(defaultPageSize: 20, maxPageSize: 100);
        var priorities = new[] { TaskPriority.High, TaskPriority.Low };

        await handler.HandleAsync(
            new ListTasksRequest(1, 20, TaskStatusFilter.Pending, priorities, Overdue: true, Search: "relatório"),
            CancellationToken.None);

        await _repository.Received(1).ListByOwnerAsync(
            _ownerId,
            1,
            20,
            Arg.Is<TaskListFilter>(filter =>
                filter.Status == TaskStatusFilter.Pending &&
                filter.Priorities.SequenceEqual(priorities) &&
                filter.Overdue == true &&
                filter.Search == "relatório"),
            Arg.Any<CancellationToken>());
    }

    [Theory] // CA-17 — search vazio ou só espaços é tratado como ausente
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_ComSearchVazioOuSoEspacos_TrataComoAusente(string search)
    {
        var handler = CreateHandler(defaultPageSize: 20, maxPageSize: 100);

        await handler.HandleAsync(new ListTasksRequest(1, 20, Search: search), CancellationToken.None);

        await _repository.Received(1).ListByOwnerAsync(
            _ownerId, 1, 20, Arg.Is<TaskListFilter>(filter => filter.Search == null), Arg.Any<CancellationToken>());
    }

    [Fact] // BE-22 — search com espaços nas bordas chega ao filtro já com Trim()
    public async Task HandleAsync_ComSearchComEspacosNasBordas_AplicaTrim()
    {
        var handler = CreateHandler(defaultPageSize: 20, maxPageSize: 100);

        await handler.HandleAsync(new ListTasksRequest(1, 20, Search: "  relatório  "), CancellationToken.None);

        await _repository.Received(1).ListByOwnerAsync(
            _ownerId, 1, 20, Arg.Is<TaskListFilter>(filter => filter.Search == "relatório"), Arg.Any<CancellationToken>());
    }

    [Fact] // CA-33c — o "hoje" do filtro (overdue) é exatamente o mesmo usado na projeção (isOverdue)
    public async Task HandleAsync_UsaOMesmoHojeNoFiltroENaProjecao()
    {
        var clientToday = new DateOnly(2026, 8, 20);
        _clientDate.Today.Returns(clientToday);

        // Vence exatamente em "hoje": se o filtro e a projeção usassem datas
        // diferentes, um dos dois discordaria sobre isOverdue.
        var task = TodoTask.Create(_ownerId, "Tarefa", null, null, clientToday, TimeProvider.System).Value;
        _repository.ListByOwnerAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<TaskListFilter>(), Arg.Any<CancellationToken>())
            .Returns((new[] { task }, 1));

        var handler = CreateHandler(defaultPageSize: 20, maxPageSize: 100);

        await handler.HandleAsync(new ListTasksRequest(1, 20, Overdue: false), CancellationToken.None);

        await _repository.Received(1).ListByOwnerAsync(
            _ownerId, 1, 20, Arg.Is<TaskListFilter>(filter => filter.Today == clientToday), Arg.Any<CancellationToken>());
    }

    private ListTasksHandler CreateHandler(int defaultPageSize, int maxPageSize) =>
        new(
            _repository,
            _currentUser,
            _clientDate,
            Options.Create(new PagingOptions { DefaultPageSize = defaultPageSize, MaxPageSize = maxPageSize }));
}
