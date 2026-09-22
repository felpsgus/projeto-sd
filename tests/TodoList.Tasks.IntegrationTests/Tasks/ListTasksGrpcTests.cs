using FluentAssertions;
using Grpc.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TodoList.Contracts.Tasks.V1;
using TodoList.Tasks.Domain.Tasks;
using Xunit;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// <c>ListTasks</c> gRPC (BE-41, recorte de BE-22) — CA-02 a CA-10. Não fala
/// com o Identity: nenhuma das duas rotas de leitura desta task valida o
/// dono (isso só acontece na criação, BE-28) — por isso
/// <see cref="TasksApiFactory"/> aqui sempre recebe <c>identityFactory: null</c>
/// e um endereço que nunca é chamado.
/// </summary>
public sealed class ListTasksGrpcTests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _connection = null!;
    private FakeTimeProvider _timeProvider = null!;
    private TasksApiFactory _factory = null!;
    private TasksGrpcTestClient _client = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero));
        _factory = new TasksApiFactory(_connection, _timeProvider, identityFactory: null, new Uri("http://identity.test"));

        await _factory.EnsureDatabaseCreatedAsync();
        _client = new TasksGrpcTestClient(_factory.Server.CreateHandler(), _factory.Server.BaseAddress);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // CA1001: a disposição de verdade acontece em DisposeAsync.
    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-02 — dois donos populados, cada um só vê as próprias
    public async Task ListTasks_ComDoisDonosPopulados_RetornaSoAsTarefasDoDonoCorrente()
    {
        var meuId = Guid.NewGuid();
        var outroId = Guid.NewGuid();
        await SeedAsync(meuId, "Minha tarefa");
        await SeedAsync(outroId, "Tarefa alheia");

        var reply = await ListAsync(meuId);

        reply.Items.Should().ContainSingle(item => item.Title == "Minha tarefa");
        reply.Items.Should().NotContain(item => item.Title == "Tarefa alheia");
    }

    [Fact] // CA-03 — soft delete nunca aparece
    public async Task ListTasks_ComTarefaRemovida_NaoAparece()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Será removida");
        await SoftDeleteAsync(id);
        await SeedAsync(owner, "Continua ativa");

        var reply = await ListAsync(owner);

        reply.Items.Should().ContainSingle();
        reply.Items[0].Title.Should().Be("Continua ativa");
    }

    [Fact] // CA-04 — sem tarefas, lista vazia, nunca erro
    public async Task ListTasks_SemTarefas_RetornaListaVaziaComTotalCountZero()
    {
        var reply = await ListAsync(Guid.NewGuid());

        reply.Items.Should().BeEmpty();
        reply.TotalCount.Should().Be(0);
    }

    [Fact] // CA-05 — mais recente primeiro
    public async Task ListTasks_ComVariasTarefas_OrdenaPorCriacaoDecrescente()
    {
        var owner = Guid.NewGuid();
        await SeedAsync(owner, "Primeira");
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        await SeedAsync(owner, "Segunda");
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        await SeedAsync(owner, "Terceira");

        var reply = await ListAsync(owner);

        reply.Items.Select(item => item.Title).Should().Equal("Terceira", "Segunda", "Primeira");
    }

    [Fact] // CA-06 — sem page/pageSize (0), aplica page=1 e Paging:DefaultPageSize
    public async Task ListTasks_SemPageNemPageSize_AplicaOPadrao()
    {
        var owner = Guid.NewGuid();
        await SeedAsync(owner, "Única");

        var reply = await ListAsync(owner, page: 0, pageSize: 0);

        reply.Page.Should().Be(1);
        reply.PageSize.Should().Be(20);
    }

    [Theory] // CA-07 — fora dos limites é InvalidArgument, nunca truncado
    [InlineData(-1, 20)]
    [InlineData(1, -1)]
    [InlineData(1, 101)]
    public async Task ListTasks_ComPageOuPageSizeForaDoLimite_DevolveInvalidArgument(int page, int pageSize)
    {
        var exception = await ListAndCaptureFailureAsync(Guid.NewGuid(), page, pageSize);

        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact] // CA-08 — total_count é o total do dono, não o da página
    public async Task ListTasks_ComMaisTarefasQueAPagina_TotalCountReflete_OTotalDoDono()
    {
        var owner = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            await SeedAsync(owner, $"Tarefa {i}");
        }

        var reply = await ListAsync(owner, page: 1, pageSize: 2);

        reply.Items.Should().HaveCount(2);
        reply.TotalCount.Should().Be(5);
    }

    [Fact] // CA-09 — percorrer todas as páginas devolve cada tarefa uma única vez
    public async Task ListTasks_PercorrendoTodasAsPaginas_DevolveCadaTarefaExatamenteUmaVez()
    {
        var owner = Guid.NewGuid();
        var idsEsperados = new List<Guid>();
        for (var i = 0; i < 25; i++)
        {
            idsEsperados.Add(await SeedAsync(owner, $"Tarefa {i:00}"));
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        var idsColetados = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var reply = await ListAsync(owner, page, pageSize: 10);
            idsColetados.AddRange(reply.Items.Select(item => Guid.Parse(item.Id)));
        }

        idsColetados.Should().HaveCount(25);
        idsColetados.Distinct().Should().HaveCount(25);
        idsColetados.Should().BeEquivalentTo(idsEsperados);
    }

    [Fact] // CA-10 — isOverdue usa x-client-date, não a data UTC do servidor
    public async Task ListTasks_ComXClientDate_IsOverdueUsaADataLocalDoUsuario()
    {
        var owner = Guid.NewGuid();
        await SeedAsync(owner, "Vence em 2026-08-20", dueDate: new DateOnly(2026, 8, 20));

        // Relógio do servidor em 2026-08-21T12:00 UTC (venceria); o usuário
        // ainda está em 2026-08-20 — não deveria estar atrasada.
        var headers = TasksGrpcTestClient.OwnerHeaders(owner.ToString(), clientDate: "2026-08-20");
        var reply = await _client.ListTasksAsync(new ListTasksRequest(), headers);

        reply.Items.Should().ContainSingle();
        reply.Items[0].IsOverdue.Should().BeFalse();
    }

    private async Task<Guid> SeedAsync(Guid owner, string title, DateOnly? dueDate = null)
    {
        await using var context = _factory.CreateDbContext();
        var task = TodoTask.Create(owner, title, null, null, dueDate, _timeProvider).Value;
        context.Tasks.Add(task);
        await context.SaveChangesAsync();

        return task.Id;
    }

    private async Task SoftDeleteAsync(Guid taskId)
    {
        await using var context = _factory.CreateDbContext();
        var task = await context.Tasks.SingleAsync(t => t.Id == taskId);
        task.SoftDelete(_timeProvider);
        await context.SaveChangesAsync();
    }

    private async Task<ListTasksReply> ListAsync(Guid owner, int page = 0, int pageSize = 0)
    {
        var headers = TasksGrpcTestClient.OwnerHeaders(owner.ToString());
        return await _client.ListTasksAsync(new ListTasksRequest { Page = page, PageSize = pageSize }, headers);
    }

    private async Task<RpcException> ListAndCaptureFailureAsync(Guid owner, int page, int pageSize)
    {
        try
        {
            await ListAsync(owner, page, pageSize);
            throw new InvalidOperationException("Esperava RpcException de falha, mas a chamada teve sucesso.");
        }
        catch (RpcException exception)
        {
            return exception;
        }
    }
}
