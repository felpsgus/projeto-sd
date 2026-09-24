using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TodoList.Contracts.Tasks.V1;
using Xunit;
using DomainTaskPriority = TodoList.Tasks.Domain.Tasks.TaskPriority;
using DomainTodoTask = TodoList.Tasks.Domain.Tasks.TodoTask;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// Dataset determinístico reaproveitado pelos testes de filtro, busca e
/// ordenação de <c>ListTasks</c> (BE-22, nota de "Testes obrigatórios": "um
/// <c>ICollectionFixture</c> com um dataset determinístico ... reaproveitado
/// pelos testes de filtro e ordenação"). Sobe <b>um único</b>
/// <see cref="TasksApiFactory"/> (SQLite in-memory, mesmo padrão de
/// <see cref="ListTasksGrpcTests"/>) para toda a coleção — mais barato que um
/// factory por teste — e semeia, uma única vez, oito tarefas de um único
/// dono (<see cref="DatasetOwnerId"/>) cobrindo as combinações de RN-LIST-06
/// (pendente/concluída × com/sem vencimento × vencimentos empatados) e de
/// busca textual (RN-LIST-05/D-16/CA-15).
///
/// <para>
/// "Hoje" (<see cref="Today"/>) é fixo em <c>2026-01-10</c> — todo teste que
/// consulta este dataset deve mandar <c>x-client-date: 2026-01-10</c> (ver
/// <see cref="TodayHeader"/>) para que os cálculos de atraso documentados
/// abaixo (na ordem de criação de cada tarefa) valham.
/// </para>
///
/// <para>
/// Como o filtro base de <c>ListTasks</c> sempre escopa por dono, testes que
/// precisam de um cenário próprio (ex.: dois donos, paginação com 25 itens,
/// fuso do CA-33b) usam <see cref="SeedOwnedTasksAsync"/> com um
/// <see cref="Guid.NewGuid"/> próprio — sem interferir neste dataset
/// compartilhado, sobre o mesmo <see cref="TasksApiFactory"/> (evita content
/// subir/derrubar o host uma vez por teste).
/// </para>
/// </summary>
public sealed class ListTasksDatasetFixture : IAsyncLifetime, IDisposable
{
    /// <summary>"Hoje" fixo usado por todo o dataset compartilhado (D-18).</summary>
    public static readonly DateOnly Today = new(2026, 1, 10);

    public const string TodayHeader = "2026-01-10";

    public static readonly Guid DatasetOwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private SqliteConnection _connection = null!;
    private FakeTimeProvider _timeProvider = null!;

    internal TasksApiFactory Factory { get; private set; } = null!;

    internal TasksGrpcTestClient Client { get; private set; } = null!;

    /// <summary>
    /// Ids das oito tarefas do dataset compartilhado, na ordem de criação
    /// (índice 0 é "Preparar relatório anual", criada primeiro). Útil para
    /// testes que precisam correlacionar um item da resposta a uma posição
    /// conhecida sem depender só do título.
    /// </summary>
    public IReadOnlyList<Guid> TaskIds { get; private set; } = [];

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        Factory = new TasksApiFactory(_connection, _timeProvider, identityFactory: null, new Uri("http://identity.test"));
        await Factory.EnsureDatabaseCreatedAsync();

        Client = new TasksGrpcTestClient(Factory.Server.CreateHandler(), Factory.Server.BaseAddress);

        await SeedSharedDatasetAsync();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // CA1001: a disposição de verdade acontece em DisposeAsync (mesmo padrão
    // de ListTasksGrpcTests) — xUnit chama IAsyncLifetime.DisposeAsync, nunca
    // este Dispose síncrono.
    public void Dispose() => GC.SuppressFinalize(this);

    /// <summary>
    /// Chamada <c>ListTasks</c> com o dono/cabeçalhos padrão deste dataset —
    /// atalho usado por praticamente todo teste de filtro/ordenação.
    /// </summary>
    public Task<ListTasksReply> ListDatasetAsync(
        TaskStatusFilter status = TaskStatusFilter.Unspecified,
        IEnumerable<TaskPriority>? priorities = null,
        bool? overdue = null,
        string? search = null,
        int page = 0,
        int pageSize = 0) =>
        ListAsync(DatasetOwnerId, TodayHeader, status, priorities, overdue, search, page, pageSize);

    /// <summary>Mesma chamada de <see cref="ListDatasetAsync"/>, para um dono/data arbitrários (cenários isolados).</summary>
    public async Task<ListTasksReply> ListAsync(
        Guid owner,
        string? clientDate,
        TaskStatusFilter status = TaskStatusFilter.Unspecified,
        IEnumerable<TaskPriority>? priorities = null,
        bool? overdue = null,
        string? search = null,
        int page = 0,
        int pageSize = 0)
    {
        var request = new ListTasksRequest
        {
            Page = page,
            PageSize = pageSize,
            Status = status,
            Search = search ?? string.Empty,
        };

        if (priorities is not null)
        {
            request.Priority.AddRange(priorities);
        }

        if (overdue is not null)
        {
            request.Overdue = overdue.Value;
        }

        var headers = TasksGrpcTestClient.OwnerHeaders(owner.ToString(), clientDate);

        return await Client.ListTasksAsync(request, headers);
    }

    /// <summary>
    /// Semeia tarefas para um dono próprio (não o dataset compartilhado),
    /// sobre o mesmo banco/host desta fixture — para cenários isolados
    /// (dois donos, paginação com N itens, fuso, soft delete).
    /// </summary>
    public async Task<Guid> SeedOwnedTaskAsync(
        Guid owner, string title, DomainTaskPriority priority = DomainTaskPriority.Medium, DateOnly? dueDate = null, bool completed = false, string? description = null)
    {
        await using var context = Factory.CreateDbContext();
        var task = DomainTodoTask.Create(owner, title, description, priority, dueDate, _timeProvider).Value;

        if (completed)
        {
            task.Complete(_timeProvider);
        }

        context.Tasks.Add(task);
        await context.SaveChangesAsync();
        _timeProvider.Advance(TimeSpan.FromSeconds(1));

        return task.Id;
    }

    public async Task SoftDeleteAsync(Guid taskId)
    {
        await using var context = Factory.CreateDbContext();
        var task = await context.Tasks.SingleAsync(t => t.Id == taskId);
        task.SoftDelete(_timeProvider);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Semeia as oito tarefas do dataset compartilhado, nesta ordem exata
    /// (a ordem de criação importa para o desempate de RN-LIST-06):
    /// <list type="number">
    /// <item>"Preparar relatório anual" — Pending, High, vence 2026-01-05 (atrasada).</item>
    /// <item>"Revisar contrato de fornecedor" — Pending, High, vence 2026-01-05 (empate de vencimento com #1; #1 foi criada primeiro).</item>
    /// <item>"Organizar mesa" — Pending, Medium, sem vencimento; descrição "tarefa simples do dia a dia".</item>
    /// <item>"Ligar para o cliente sobre Relatório mensal" — Pending, Low, vence 2026-01-20 (futura); "R" maiúsculo prova a busca case-insensitive (CA-14) sem depender de fold de acento (o caractere acentuado "ó" já nasce minúsculo dos dois lados — SQLite, usado por este dataset, só faz fold de maiúscula/minúscula ASCII, não de caracteres acentuados).</item>
    /// <item>"Pagar boleto" — Pending, Medium, vence 2026-01-10 (hoje — não atrasada).</item>
    /// <item>"Arquivo especial" — Completed, High, venceu 2026-01-02 (mas concluída, nunca atrasada); descrição "Contém 100% de progresso e nome_arquivo.txt" (literal % e _, CA-15).</item>
    /// <item>"Comprar material de escritório" — Completed, Medium, sem vencimento.</item>
    /// <item>"Enviar valores" — Completed, Low, venceu 2025-12-20; descrição "Ver relatório de vendas" (busca por descrição, CA-13).</item>
    /// </list>
    /// Com "hoje" = 2026-01-10 (<see cref="Today"/>), só #1 e #2 são atrasadas
    /// (RN-TASK-16: Pending e vencimento anterior a hoje).
    /// </summary>
    private async Task SeedSharedDatasetAsync()
    {
        var ids = new List<Guid>
        {
            await SeedOwnedTaskAsync(DatasetOwnerId, "Preparar relatório anual", DomainTaskPriority.High, new DateOnly(2026, 1, 5)),
            await SeedOwnedTaskAsync(DatasetOwnerId, "Revisar contrato de fornecedor", DomainTaskPriority.High, new DateOnly(2026, 1, 5)),
            await SeedOwnedTaskAsync(DatasetOwnerId, "Organizar mesa", DomainTaskPriority.Medium, dueDate: null, description: "tarefa simples do dia a dia"),
            await SeedOwnedTaskAsync(DatasetOwnerId, "Ligar para o cliente sobre Relatório mensal", DomainTaskPriority.Low, new DateOnly(2026, 1, 20)),
            await SeedOwnedTaskAsync(DatasetOwnerId, "Pagar boleto", DomainTaskPriority.Medium, new DateOnly(2026, 1, 10)),
            await SeedOwnedTaskAsync(
                DatasetOwnerId, "Arquivo especial", DomainTaskPriority.High, new DateOnly(2026, 1, 2), completed: true,
                description: "Contém 100% de progresso e nome_arquivo.txt"),
            await SeedOwnedTaskAsync(DatasetOwnerId, "Comprar material de escritório", DomainTaskPriority.Medium, dueDate: null, completed: true),
            await SeedOwnedTaskAsync(
                DatasetOwnerId, "Enviar valores", DomainTaskPriority.Low, new DateOnly(2025, 12, 20), completed: true,
                description: "Ver relatório de vendas"),
        };

        TaskIds = ids;
    }
}

/// <summary>
/// Coleção xUnit para o dataset compartilhado de <c>ListTasks</c> (BE-22) —
/// sobe uma única vez para todos os testes marcados
/// <c>[Collection("ListTasksDataset")]</c>.
/// </summary>
[CollectionDefinition("ListTasksDataset")]
public sealed class ListTasksDatasetCollectionDefinition : ICollectionFixture<ListTasksDatasetFixture>
{
}
