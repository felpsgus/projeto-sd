using FluentAssertions;
using Grpc.Core;
using TodoList.Contracts.Tasks.V1;
using Xunit;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// <c>ListTasks</c> gRPC — filtros, busca, ordenação e paginação (BE-22,
/// CA-01 a CA-35, exceto CA-35 que é medição de desempenho documentada à
/// parte no PR). Reaproveita o dataset determinístico de
/// <see cref="ListTasksDatasetFixture"/> para os testes de filtro e
/// ordenação (nota de "Testes obrigatórios" de BE-22); cenários que
/// precisam de um dono/dataset próprio (paginação com N itens, dois donos,
/// fuso, soft delete, valores inválidos) semeiam sua própria tarefa via
/// <see cref="ListTasksDatasetFixture.SeedOwnedTaskAsync"/>, sobre o mesmo
/// host — sem interferir no dataset compartilhado, já que tudo é escopado
/// por dono.
/// </summary>
[Collection("ListTasksDataset")]
public sealed class ListTasksFiltersGrpcTests
{
    private readonly ListTasksDatasetFixture _fixture;

    public ListTasksFiltersGrpcTests(ListTasksDatasetFixture fixture)
    {
        _fixture = fixture;
    }

    // ---- Base e escopo -----------------------------------------------

    [Fact] // CA-01 — só as tarefas do dono autenticado
    public async Task ListTasks_ComDoisDonosPopulados_RetornaSoAsTarefasDoDonoCorrente()
    {
        var meuId = Guid.NewGuid();
        var outroId = Guid.NewGuid();
        await _fixture.SeedOwnedTaskAsync(meuId, "Minha tarefa");
        await _fixture.SeedOwnedTaskAsync(outroId, "Tarefa alheia");

        var reply = await _fixture.ListAsync(meuId, clientDate: null);

        reply.Items.Should().ContainSingle(item => item.Title == "Minha tarefa");
        reply.Items.Should().NotContain(item => item.Title == "Tarefa alheia");
    }

    [Fact] // CA-02 — soft delete nunca aparece, mesmo com status=all
    public async Task ListTasks_ComTarefaRemovida_NaoApareceMesmoComStatusAll()
    {
        var owner = Guid.NewGuid();
        var removidaId = await _fixture.SeedOwnedTaskAsync(owner, "Será removida");
        await _fixture.SoftDeleteAsync(removidaId);
        await _fixture.SeedOwnedTaskAsync(owner, "Continua ativa");

        var reply = await _fixture.ListAsync(owner, clientDate: null, status: TaskStatusFilter.All);

        reply.Items.Should().ContainSingle();
        reply.Items[0].Title.Should().Be("Continua ativa");
    }

    [Fact] // CA-04 — sem tarefas, 200 com items vazio e totalItems/totalPages zero (aqui: total_count)
    public async Task ListTasks_SemTarefas_RetornaListaVaziaComTotalCountZero()
    {
        var reply = await _fixture.ListAsync(Guid.NewGuid(), clientDate: null);

        reply.Items.Should().BeEmpty();
        reply.TotalCount.Should().Be(0);
    }

    // ---- Filtros --------------------------------------------------------

    [Fact] // CA-05 — status=pending só pendentes
    public async Task ListTasks_ComStatusPending_RetornaSoPendentes()
    {
        var reply = await _fixture.ListDatasetAsync(status: TaskStatusFilter.Pending, pageSize: 20);

        reply.Items.Should().HaveCount(5);
        reply.Items.Should().OnlyContain(item => item.Status == TodoList.Contracts.Tasks.V1.TaskStatus.Pending);
    }

    [Fact] // CA-05 — status=completed só concluídas
    public async Task ListTasks_ComStatusCompleted_RetornaSoConcluidas()
    {
        var reply = await _fixture.ListDatasetAsync(status: TaskStatusFilter.Completed, pageSize: 20);

        reply.Items.Should().HaveCount(3);
        reply.Items.Should().OnlyContain(item => item.Status == TodoList.Contracts.Tasks.V1.TaskStatus.Completed);
    }

    [Fact] // CA-05 — status=all (e ausência do parâmetro) retorna ambas
    public async Task ListTasks_SemStatusOuComStatusAll_RetornaPendentesEConcluidas()
    {
        var semParametro = await _fixture.ListDatasetAsync(pageSize: 20);
        var comAll = await _fixture.ListDatasetAsync(status: TaskStatusFilter.All, pageSize: 20);

        semParametro.Items.Should().HaveCount(8);
        comAll.Items.Should().HaveCount(8);
    }

    [Fact] // CA-06 — priority=high só as de prioridade alta
    public async Task ListTasks_ComPriorityHigh_RetornaSoAsDeAltaPrioridade()
    {
        var reply = await _fixture.ListDatasetAsync(priorities: [TaskPriority.High], pageSize: 20);

        reply.Items.Should().HaveCount(3);
        reply.Items.Should().OnlyContain(item => item.Priority == TaskPriority.High);
    }

    [Fact] // CA-07 — priority=low&priority=high retorna as duas, nenhuma medium
    public async Task ListTasks_ComPriorityLowEHigh_RetornaAsDuasENenhumaMedium()
    {
        var reply = await _fixture.ListDatasetAsync(priorities: [TaskPriority.Low, TaskPriority.High], pageSize: 20);

        reply.Items.Should().HaveCount(5);
        reply.Items.Should().OnlyContain(item => item.Priority == TaskPriority.Low || item.Priority == TaskPriority.High);
    }

    [Fact] // CA-08 — overdue=true só pendentes com vencimento anterior a hoje
    public async Task ListTasks_ComOverdueTrue_RetornaSoPendentesVencidas()
    {
        var reply = await _fixture.ListDatasetAsync(overdue: true, pageSize: 20);

        reply.Items.Should().HaveCount(2);
        reply.Items.Should().OnlyContain(item => item.IsOverdue);
        reply.Items.Select(item => item.Title).Should().BeEquivalentTo(
            "Preparar relatório anual", "Revisar contrato de fornecedor");
    }

    [Fact] // CA-09 — overdue=false retorna as não atrasadas
    public async Task ListTasks_ComOverdueFalse_RetornaAsNaoAtrasadas()
    {
        var reply = await _fixture.ListDatasetAsync(overdue: false, pageSize: 20);

        reply.Items.Should().HaveCount(6);
        reply.Items.Should().OnlyContain(item => !item.IsOverdue);
    }

    [Fact] // CA-10 — filtros combinam por conjunção
    public async Task ListTasks_ComStatusPriorityEOverdueCombinados_AplicaOsTresSimultaneamente()
    {
        var reply = await _fixture.ListDatasetAsync(
            status: TaskStatusFilter.Pending, priorities: [TaskPriority.High], overdue: true, pageSize: 20);

        reply.Items.Should().HaveCount(2);
        reply.Items.Should().OnlyContain(item =>
            item.Status == TodoList.Contracts.Tasks.V1.TaskStatus.Pending && item.Priority == TaskPriority.High && item.IsOverdue);
    }

    [Fact] // CA-11 — status fora do enum é InvalidArgument, não ignorado
    public async Task ListTasks_ComStatusInvalido_DevolveInvalidArgument()
    {
        var exception = await CaptureFailureAsync(
            () => _fixture.ListAsync(Guid.NewGuid(), clientDate: null, status: (TaskStatusFilter)99));

        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact] // CA-11 — priority fora do enum é InvalidArgument, não ignorado
    public async Task ListTasks_ComPriorityInvalida_DevolveInvalidArgument()
    {
        var exception = await CaptureFailureAsync(
            () => _fixture.ListAsync(Guid.NewGuid(), clientDate: null, priorities: [(TaskPriority)99]));

        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    // ---- Busca ------------------------------------------------------------

    [Fact] // CA-12/CA-13/CA-14 — busca por título, descrição e case-insensitive, numa só asserção
    public async Task ListTasks_ComSearchRelatorio_EncontraPorTituloEDescricaoIndependenteDeCaixa()
    {
        var reply = await _fixture.ListDatasetAsync(search: "relatório", pageSize: 20);

        reply.Items.Should().HaveCount(3);
        reply.Items.Select(item => item.Title).Should().BeEquivalentTo(
            "Preparar relatório anual", // título, minúsculo/acentuado
            "Ligar para o cliente sobre Relatório mensal", // "R" maiúsculo — prova CA-14 (case-insensitive)
            "Enviar valores"); // via descrição "Ver relatório de vendas" — prova CA-13
    }

    [Fact] // CA-15 — "%" não funciona como curinga, só encontra o caractere literal
    public async Task ListTasks_ComSearchPorcento_EncontraSoOCaractereLiteral()
    {
        var reply = await _fixture.ListDatasetAsync(search: "%", pageSize: 20);

        reply.Items.Should().ContainSingle();
        reply.Items[0].Title.Should().Be("Arquivo especial");
    }

    [Fact] // CA-15 — "_" não funciona como curinga, só encontra o caractere literal
    public async Task ListTasks_ComSearchUnderscore_EncontraSoOCaractereLiteral()
    {
        var reply = await _fixture.ListDatasetAsync(search: "_", pageSize: 20);

        reply.Items.Should().ContainSingle();
        reply.Items[0].Title.Should().Be("Arquivo especial");
    }

    [Fact] // CA-16 — search combina com os demais filtros
    public async Task ListTasks_ComSearchEStatus_CombinaOsDoisFiltros()
    {
        var reply = await _fixture.ListDatasetAsync(search: "relatório", status: TaskStatusFilter.Completed, pageSize: 20);

        reply.Items.Should().ContainSingle();
        reply.Items[0].Title.Should().Be("Enviar valores");
    }

    [Theory] // CA-17 — search vazio ou só espaços é tratado como ausente
    [InlineData("")]
    [InlineData("   ")]
    public async Task ListTasks_ComSearchVazioOuSoEspacos_TrataComoAusente(string search)
    {
        var reply = await _fixture.ListDatasetAsync(search: search, pageSize: 20);

        reply.Items.Should().HaveCount(8);
    }

    // ---- Ordenação ----------------------------------------------------

    [Fact] // CA-18 a CA-23 — a ordem completa do dataset, numa única asserção de sequência
    public async Task ListTasks_SemFiltro_OrdenaConformeRnList06()
    {
        var reply = await _fixture.ListDatasetAsync(pageSize: 20);

        reply.Items.Select(item => item.Title).Should().Equal(
            "Preparar relatório anual", // Pending, vence 2026-01-05 (empate com a próxima; criada primeiro)
            "Revisar contrato de fornecedor", // Pending, vence 2026-01-05
            "Pagar boleto", // Pending, vence 2026-01-10
            "Ligar para o cliente sobre Relatório mensal", // Pending, vence 2026-01-20
            "Organizar mesa", // Pending, sem vencimento — por último entre as pendentes
            "Enviar valores", // Completed, venceu 2025-12-20
            "Arquivo especial", // Completed, venceu 2026-01-02
            "Comprar material de escritório"); // Completed, sem vencimento — por último entre as concluídas
    }

    [Fact] // CA-22 — a ordenação é estável e determinística ao repetir a mesma consulta
    public async Task ListTasks_RepetidoVariasVezes_DevolveSempreAMesmaSequencia()
    {
        var primeira = await _fixture.ListDatasetAsync(pageSize: 20);

        for (var i = 0; i < 5; i++)
        {
            var repeticao = await _fixture.ListDatasetAsync(pageSize: 20);
            repeticao.Items.Select(item => item.Id).Should().Equal(primeira.Items.Select(item => item.Id));
        }
    }

    // ---- Paginação ------------------------------------------------------

    [Fact] // CA-24 — sem page/pageSize, página 1 com 20 itens (aqui, os 8 do dataset cabem numa página só)
    public async Task ListTasks_SemPageNemPageSize_AplicaOPadrao()
    {
        var reply = await _fixture.ListDatasetAsync();

        reply.Page.Should().Be(1);
        reply.PageSize.Should().Be(20);
        reply.Items.Should().HaveCount(8);
    }

    [Fact] // CA-25 — total_count reflete o total após os filtros, não o total geral do dono
    public async Task ListTasks_ComFiltro_TotalCountReflete_OTotalAposOFiltro()
    {
        var reply = await _fixture.ListDatasetAsync(status: TaskStatusFilter.Completed, pageSize: 20);

        reply.TotalCount.Should().Be(3);
    }

    [Fact] // CA-27 — percorrer todas as páginas devolve cada tarefa exatamente uma vez
    public async Task ListTasks_PercorrendoTodasAsPaginas_DevolveCadaTarefaExatamenteUmaVez()
    {
        var owner = Guid.NewGuid();
        var idsEsperados = new List<Guid>();
        for (var i = 0; i < 25; i++)
        {
            idsEsperados.Add(await _fixture.SeedOwnedTaskAsync(owner, $"Tarefa {i:00}"));
        }

        var idsColetados = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var reply = await _fixture.ListAsync(owner, clientDate: null, page: page, pageSize: 10);
            idsColetados.AddRange(reply.Items.Select(item => Guid.Parse(item.Id)));
        }

        idsColetados.Should().HaveCount(25);
        idsColetados.Distinct().Should().HaveCount(25);
        idsColetados.Should().BeEquivalentTo(idsEsperados);
    }

    [Fact] // CA-28 — página além do total retorna 200 com items vazio e total_count correto
    public async Task ListTasks_ComPageAlemDoTotal_RetornaListaVaziaComTotalCountCorreto()
    {
        var reply = await _fixture.ListDatasetAsync(page: 99, pageSize: 20);

        reply.Items.Should().BeEmpty();
        reply.TotalCount.Should().Be(8);
    }

    [Fact] // CA-29 — pageSize acima do máximo configurado retorna InvalidArgument
    public async Task ListTasks_ComPageSizeAcimaDoMaximo_DevolveInvalidArgument()
    {
        var exception = await CaptureFailureAsync(() => _fixture.ListDatasetAsync(pageSize: 101));

        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Theory] // CA-30 — page=0/-1 ou pageSize=0 retornam InvalidArgument
    [InlineData(-1, 20)]
    [InlineData(1, -1)]
    public async Task ListTasks_ComPageOuPageSizeInvalidos_DevolveInvalidArgument(int page, int pageSize)
    {
        var exception = await CaptureFailureAsync(() => _fixture.ListDatasetAsync(page: page, pageSize: pageSize));

        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    // ---- Fuso e coerência filtro/projeção (D-18) -------------------------

    [Fact] // CA-33b — @today do WHERE é IClientDate (x-client-date), não a data UTC do servidor
    public async Task ListTasks_ComXClientDateDiferenteDaDataUtc_NaoMarcaComoAtrasadaAntesDaHora()
    {
        var owner = Guid.NewGuid();
        // Vence em 2026-08-20; o relógio do servidor pode já estar em
        // 2026-08-21 (madrugada UTC), mas o cliente ainda está em 2026-08-20.
        await _fixture.SeedOwnedTaskAsync(owner, "Vence em 2026-08-20", dueDate: new DateOnly(2026, 8, 20));

        var reply = await _fixture.ListAsync(owner, clientDate: "2026-08-20", overdue: true);
        var replyIsOverdueFalse = await _fixture.ListAsync(owner, clientDate: "2026-08-20");

        reply.Items.Should().BeEmpty("a tarefa vence 'hoje' na data do cliente, não é atrasada ainda");
        replyIsOverdueFalse.Items.Should().ContainSingle();
        replyIsOverdueFalse.Items[0].IsOverdue.Should().BeFalse();
    }

    [Fact] // CA-33c — nenhum item de overdue=true tem isOverdue=false, e vice-versa
    public async Task ListTasks_ComOverdueTrue_TodosOsItensTemIsOverdueTrue()
    {
        var overdueTrue = await _fixture.ListDatasetAsync(overdue: true, pageSize: 20);
        var overdueFalse = await _fixture.ListDatasetAsync(overdue: false, pageSize: 20);

        overdueTrue.Items.Should().NotBeEmpty();
        overdueTrue.Items.Should().OnlyContain(item => item.IsOverdue);
        overdueFalse.Items.Should().OnlyContain(item => !item.IsOverdue);
    }

    private static async Task<RpcException> CaptureFailureAsync(Func<Task<ListTasksReply>> call)
    {
        try
        {
            await call();
            throw new InvalidOperationException("Esperava RpcException de falha, mas a chamada teve sucesso.");
        }
        catch (RpcException exception)
        {
            return exception;
        }
    }
}
