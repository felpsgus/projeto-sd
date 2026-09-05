using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TodoList.Tasks.Domain.Tasks;
using TodoList.Tasks.Infrastructure.Persistence;
using TodoList.Tasks.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace TodoList.Tasks.IntegrationTests.Persistence;

/// <summary>
/// CA-18 e CA-19 de BE-05: persistência real da <b>entidade de verdade</b>
/// (<see cref="TodoTask"/>) contra o pipeline relacional real do EF Core
/// (<c>SaveChanges</c>, interceptor de auditoria/soft delete, filtro global
/// de query) sobre SQLite in-memory — mesmo padrão de
/// <see cref="AuditingAndSoftDeleteTests"/> (BE-02), que fazia isso só com a
/// entidade de teste <see cref="AuditableProbe"/> porque <c>TodoTask</c> não
/// existia ainda. Não precisa de Docker/Postgres: exatamente o que o escopo
/// desta task pede para "o que puder ser verificado sem Postgres".
/// <see cref="TodoTaskPostgresIndexTests"/> cobre o que só o Postgres real
/// prova (CA-20, catálogo de índices).
/// </summary>
public class TodoTaskSqlitePersistenceTests : IAsyncLifetime, IDisposable
{
    private static readonly Guid _ownerId = Guid.NewGuid();

    private SqliteConnection _connection = null!;
    private FakeTimeProvider _timeProvider = null!;

    public Task InitializeAsync()
    {
        // "DataSource=:memory:" cria um banco novo por conexão; mantê-la
        // aberta pelo tempo de vida do teste é o que faz o banco sobreviver
        // entre os vários DbContext criados dentro do mesmo teste.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // CA1001: o tipo tem um campo descartável (_connection); a disposição de
    // verdade acontece em DisposeAsync (IAsyncLifetime).
    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-18
    public async Task SaveChangesAsync_TodoTaskCompleta_RecarregadaPreservaTodosOsCampos()
    {
        // Arrange
        var dueDate = new DateOnly(2026, 3, 20);
        var task = TodoTask.Create(_ownerId, "  Comprar leite  ", "Descrição da tarefa", TaskPriority.High, dueDate, _timeProvider).Value;
        task.Complete(_timeProvider);

        await using (var context = CreateContext())
        {
            context.Tasks.Add(task);
            await context.SaveChangesAsync();
        }

        // Salva o Id gerado (Guid) para reler num contexto novo — não do
        // change tracker em memória.
        var savedId = task.Id;

        // Act
        await using var novoContexto = CreateContext();
        var recarregada = await novoContexto.Tasks.SingleAsync(t => t.Id == savedId);

        // Assert
        recarregada.Id.Should().Be(task.Id);
        recarregada.OwnerId.Should().Be(_ownerId);
        recarregada.Title.Should().Be("Comprar leite", "o título é persistido com trim (CA-02)");
        recarregada.Description.Should().Be("Descrição da tarefa");
        recarregada.Status.Should().Be(TodoTaskStatus.Completed);
        recarregada.Priority.Should().Be(TaskPriority.High);
        recarregada.DueDate.Should().Be(dueDate, "DueDate volta como data, sem componente de hora");
        recarregada.CompletedAt.Should().NotBeNull();
        recarregada.CreatedAt.Should().Be(_timeProvider.GetUtcNow().UtcDateTime);
        recarregada.UpdatedAt.Should().Be(_timeProvider.GetUtcNow().UtcDateTime);
        recarregada.DeletedAt.Should().BeNull();
    }

    [Fact] // CA-19
    public async Task SoftDelete_TarefaRemovida_NaoApareceNaConsultaNormalMasApareceComIgnoreQueryFilters()
    {
        // Arrange
        var task = TodoTask.Create(_ownerId, "Tarefa a remover", null, null, null, _timeProvider).Value;
        Guid id;

        await using (var context = CreateContext())
        {
            context.Tasks.Add(task);
            await context.SaveChangesAsync();
            id = task.Id;

            var paraRemover = await context.Tasks.SingleAsync(t => t.Id == id);
            paraRemover.SoftDelete(_timeProvider).IsSuccess.Should().BeTrue();
            await context.SaveChangesAsync();
        }

        // Act & Assert
        await using (var context = CreateContext())
        {
            var naConsultaNormal = await context.Tasks.SingleOrDefaultAsync(t => t.Id == id);
            naConsultaNormal.Should().BeNull("uma tarefa soft-deleted não aparece na consulta normal do repositório (CA-19)");

            var comFiltroIgnorado = await context.Tasks.IgnoreQueryFilters().SingleOrDefaultAsync(t => t.Id == id);
            comFiltroIgnorado.Should().NotBeNull("IgnoreQueryFilters() é o caminho explícito para revê-la (CA-19)");
            comFiltroIgnorado!.DeletedAt.Should().NotBeNull();
        }
    }

    private TasksDbContext CreateContext()
    {
        var interceptor = new AuditingSaveChangesInterceptor(_timeProvider);

        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptor)
            .Options;

        var context = new TasksDbContext(options);
        context.Database.EnsureCreated();

        return context;
    }
}
