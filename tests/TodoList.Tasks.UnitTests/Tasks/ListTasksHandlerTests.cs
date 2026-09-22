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
/// <see cref="ListTasksHandler"/> (BE-41, recorte de BE-22) com
/// <see cref="ITodoTaskRepository"/> substituído (NSubstitute) — a resolução
/// de padrão (CA-06), a validação de limites (CA-07) e a passagem do dono
/// corrente/do "hoje" do cliente para a consulta e a projeção.
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
        _repository.ListByOwnerAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
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
        await _repository.Received(1).ListByOwnerAsync(_ownerId, 1, 20, Arg.Any<CancellationToken>());
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

    [Theory] // CA-07 — page/pageSize negativos são erro de validação, não normalizados
    [InlineData(-1, 20)]
    [InlineData(1, -1)]
    public async Task HandleAsync_ComPageOuPageSizeNegativo_RetornaFalhaDeValidacao(int page, int pageSize)
    {
        var handler = CreateHandler(defaultPageSize: 20, maxPageSize: 100);

        var result = await handler.HandleAsync(new ListTasksRequest(page, pageSize), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(TodoList.SharedKernel.ErrorType.Validation);
    }

    [Fact] // CA-07 — pageSize acima do máximo configurado é erro de validação
    public async Task HandleAsync_ComPageSizeAcimaDoMaximo_RetornaTaskErrorsInvalidPageSize()
    {
        var handler = CreateHandler(defaultPageSize: 20, maxPageSize: 100);

        var result = await handler.HandleAsync(new ListTasksRequest(1, 101), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("task.invalid_page_size");
        result.Error.Message.Should().Contain("100");
    }

    [Fact] // CA-02 — a consulta usa o dono corrente, não outro
    public async Task HandleAsync_ConsultaORepositorioComODonoCorrente()
    {
        var handler = CreateHandler(defaultPageSize: 20, maxPageSize: 100);

        await handler.HandleAsync(new ListTasksRequest(1, 20), CancellationToken.None);

        await _repository.Received(1).ListByOwnerAsync(_ownerId, 1, 20, Arg.Any<CancellationToken>());
    }

    [Fact] // CA-10 — isOverdue de cada item usa IClientDate.Today, não a data do servidor
    public async Task HandleAsync_MapeiaItensUsandoODiaDoClientDate()
    {
        var task = TodoTask.Create(_ownerId, "Tarefa", null, null, new DateOnly(2025, 12, 31), TimeProvider.System).Value;
        _repository.ListByOwnerAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((new[] { task }, 1));

        var handler = CreateHandler(defaultPageSize: 20, maxPageSize: 100);

        var result = await handler.HandleAsync(new ListTasksRequest(1, 20), CancellationToken.None);

        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].IsOverdue.Should().BeTrue("_clientDate.Today (2026-01-01) é posterior ao vencimento (2025-12-31)");
    }

    [Fact] // CA-08 — total_count vem do repositório, não é recalculado a partir da página
    public async Task HandleAsync_PropagaOTotalCountDoRepositorio()
    {
        _repository.ListByOwnerAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((Array.Empty<TodoTask>(), 137));

        var handler = CreateHandler(defaultPageSize: 20, maxPageSize: 100);

        var result = await handler.HandleAsync(new ListTasksRequest(1, 20), CancellationToken.None);

        result.Value.TotalCount.Should().Be(137);
    }

    private ListTasksHandler CreateHandler(int defaultPageSize, int maxPageSize) =>
        new(
            _repository,
            _currentUser,
            _clientDate,
            Options.Create(new PagingOptions { DefaultPageSize = defaultPageSize, MaxPageSize = maxPageSize }));
}
