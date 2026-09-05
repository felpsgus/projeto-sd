using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using TodoList.Tasks.Domain.Tasks;
using Xunit;

namespace TodoList.Tasks.UnitTests.Tasks;

/// <summary>
/// Testes de unidade de <see cref="TodoTask"/> (BE-05) — todos puros: sem
/// banco, sem <see cref="DateTime.UtcNow"/> direto, sempre via
/// <see cref="FakeTimeProvider"/> avançado explicitamente. Cobre CA-01 a
/// CA-16 e CA-21.
/// </summary>
public class TodoTaskTests
{
    private static readonly Guid _ownerId = Guid.NewGuid();

    // ---------------------------------------------------------------
    // CA-01, CA-02: título
    // ---------------------------------------------------------------

    [Theory] // CA-01
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Create_TituloVazioOuSoEspacos_RetornaFalha(string titulo)
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();

        // Act
        var resultado = TodoTask.Create(_ownerId, titulo, null, null, null, timeProvider);

        // Assert
        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(TodoTaskErrors.TitleRequired);
    }

    [Fact] // CA-01
    public void Create_TituloCom201Caracteres_RetornaFalha()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var titulo = new string('a', 201);

        // Act
        var resultado = TodoTask.Create(_ownerId, titulo, null, null, null, timeProvider);

        // Assert
        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(TodoTaskErrors.TitleTooLong);
    }

    [Fact] // CA-01
    public void Create_TituloCom1Caractere_Sucesso()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();

        // Act
        var resultado = TodoTask.Create(_ownerId, "a", null, null, null, timeProvider);

        // Assert
        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Title.Should().Be("a");
    }

    [Fact] // CA-01
    public void Create_TituloCom200Caracteres_Sucesso()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var titulo = new string('a', 200);

        // Act
        var resultado = TodoTask.Create(_ownerId, titulo, null, null, null, timeProvider);

        // Assert
        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Title.Should().HaveLength(200);
    }

    [Fact] // CA-02
    public void Create_TituloComEspacosAoRedor_EhArmazenadoComTrim()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();

        // Act
        var resultado = TodoTask.Create(_ownerId, "  Comprar pão  ", null, null, null, timeProvider);

        // Assert
        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Title.Should().Be("Comprar pão");
    }

    // ---------------------------------------------------------------
    // CA-03: descrição
    // ---------------------------------------------------------------

    [Fact] // CA-03
    public void Create_DescricaoNula_Sucesso()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();

        // Act
        var resultado = TodoTask.Create(_ownerId, "Título", null, null, null, timeProvider);

        // Assert
        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Description.Should().BeNull();
    }

    [Fact] // CA-03
    public void Create_DescricaoCom2000Caracteres_Sucesso()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var descricao = new string('d', 2000);

        // Act
        var resultado = TodoTask.Create(_ownerId, "Título", descricao, null, null, timeProvider);

        // Assert
        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Description.Should().HaveLength(2000);
    }

    [Fact] // CA-03
    public void Create_DescricaoCom2001Caracteres_RetornaFalha()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var descricao = new string('d', 2001);

        // Act
        var resultado = TodoTask.Create(_ownerId, "Título", descricao, null, null, timeProvider);

        // Assert
        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(TodoTaskErrors.DescriptionTooLong);
    }

    // ---------------------------------------------------------------
    // CA-04, CA-05: prioridade
    // ---------------------------------------------------------------

    [Fact] // CA-04
    public void Create_SemPrioridade_ProduzPrioridadeMedium()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();

        // Act
        var resultado = TodoTask.Create(_ownerId, "Título", null, null, null, timeProvider);

        // Assert
        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Priority.Should().Be(TaskPriority.Medium);
    }

    [Fact] // CA-05
    public void Create_PrioridadeForaDoIntervaloDoEnum_RetornaFalha()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var prioridadeInvalida = (TaskPriority)99;

        // Act
        var resultado = TodoTask.Create(_ownerId, "Título", null, prioridadeInvalida, null, timeProvider);

        // Assert
        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(TodoTaskErrors.InvalidPriority);
    }

    // ---------------------------------------------------------------
    // CA-06: vencimento no passado
    // ---------------------------------------------------------------

    [Fact] // CA-06
    public void Create_DueDateNoPassado_Sucede_ETarefaReportaIsOverdueVerdadeiro()
    {
        // Arrange
        var hoje = new DateOnly(2026, 3, 15);
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(hoje.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var vencimentoPassado = hoje.AddDays(-1);

        // Act
        var resultado = TodoTask.Create(_ownerId, "Título", null, null, vencimentoPassado, timeProvider);

        // Assert
        resultado.IsSuccess.Should().BeTrue("RN-TASK-05/D-06: data de vencimento no passado é aceita");
        resultado.Value.DueDate.Should().Be(vencimentoPassado);
        resultado.Value.IsOverdue(hoje).Should().BeTrue();
    }

    // ---------------------------------------------------------------
    // CA-07: status/CompletedAt sempre no padrão na criação
    // ---------------------------------------------------------------

    [Theory] // CA-07
    [InlineData(null, null)]
    [InlineData(TaskPriority.High, "2030-01-01")]
    public void Create_SempreProduzStatusPendingECompletedAtNulo(TaskPriority? prioridade, string? dueDateText)
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        DateOnly? dueDate = dueDateText is null ? null : DateOnly.Parse(dueDateText);

        // Act
        var resultado = TodoTask.Create(_ownerId, "Título", "descrição qualquer", prioridade, dueDate, timeProvider);

        // Assert
        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Status.Should().Be(TodoTaskStatus.Pending);
        resultado.Value.CompletedAt.Should().BeNull();
    }

    // ---------------------------------------------------------------
    // CA-08: OwnerId obrigatório
    // ---------------------------------------------------------------

    [Fact] // CA-08
    public void Create_OwnerIdVazio_RetornaFalha()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();

        // Act
        var resultado = TodoTask.Create(Guid.Empty, "Título", null, null, null, timeProvider);

        // Assert
        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(TodoTaskErrors.OwnerRequired);
    }

    // ---------------------------------------------------------------
    // CA-09 a CA-13: ciclo de vida
    // ---------------------------------------------------------------

    [Fact] // CA-09
    public void Complete_TarefaPendente_MudaStatusParaCompletedEGravaCompletedAtDoTimeProvider()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var task = CriarTarefaValida(timeProvider);
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        // Act
        var resultado = task.Complete(timeProvider);

        // Assert
        resultado.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(TodoTaskStatus.Completed);
        task.CompletedAt.Should().Be(timeProvider.GetUtcNow().UtcDateTime);
    }

    [Fact] // CA-10, CA-13
    public void Complete_TarefaJaCompleted_RetornaFalhaComCodigoEstavelSemLancarENaoAlteraCompletedAt()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var task = CriarTarefaValida(timeProvider);
        task.Complete(timeProvider);
        var completedAtOriginal = task.CompletedAt;
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        // Act
        var acao = () => task.Complete(timeProvider);
        var resultado = acao();

        // Assert
        acao.Should().NotThrow("transição inválida é erro de negócio (Result), nunca exceção");
        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(TodoTaskErrors.AlreadyCompleted);
        task.CompletedAt.Should().Be(completedAtOriginal);
    }

    [Fact] // CA-11
    public void Reopen_TarefaCompleted_VoltaParaPendingELimpaCompletedAt()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var task = CriarTarefaValida(timeProvider);
        task.Complete(timeProvider);
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        // Act
        var resultado = task.Reopen(timeProvider);

        // Assert
        resultado.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(TodoTaskStatus.Pending);
        task.CompletedAt.Should().BeNull();
    }

    [Fact] // CA-12, CA-13
    public void Reopen_TarefaPendente_RetornaFalhaSemLancarENaoAlteraNada()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var task = CriarTarefaValida(timeProvider);
        var updatedAtOriginal = task.UpdatedAt;

        // Act
        var acao = () => task.Reopen(timeProvider);
        var resultado = acao();

        // Assert
        acao.Should().NotThrow("transição inválida é erro de negócio (Result), nunca exceção");
        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(TodoTaskErrors.NotCompleted);
        task.Status.Should().Be(TodoTaskStatus.Pending);
        task.UpdatedAt.Should().Be(updatedAtOriginal);
    }

    // ---------------------------------------------------------------
    // CA-14, CA-15: UpdatedAt
    // ---------------------------------------------------------------

    [Fact] // CA-14
    public void UpdateDetails_Sucesso_AtualizaUpdatedAtComOTimeProviderAvancado()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var task = CriarTarefaValida(timeProvider);
        timeProvider.Advance(TimeSpan.FromMinutes(10));

        // Act
        var resultado = task.UpdateDetails("Novo título", "Nova descrição", TaskPriority.High, null, timeProvider);

        // Assert
        resultado.IsSuccess.Should().BeTrue();
        task.UpdatedAt.Should().Be(timeProvider.GetUtcNow().UtcDateTime);
    }

    [Fact] // CA-14
    public void Complete_Sucesso_AtualizaUpdatedAtComOTimeProviderAvancado()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var task = CriarTarefaValida(timeProvider);
        timeProvider.Advance(TimeSpan.FromMinutes(10));

        // Act
        task.Complete(timeProvider);

        // Assert
        task.UpdatedAt.Should().Be(timeProvider.GetUtcNow().UtcDateTime);
    }

    [Fact] // CA-14
    public void Reopen_Sucesso_AtualizaUpdatedAtComOTimeProviderAvancado()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var task = CriarTarefaValida(timeProvider);
        task.Complete(timeProvider);
        timeProvider.Advance(TimeSpan.FromMinutes(10));

        // Act
        task.Reopen(timeProvider);

        // Assert
        task.UpdatedAt.Should().Be(timeProvider.GetUtcNow().UtcDateTime);
    }

    [Fact] // CA-14
    public void SoftDelete_Sucesso_AtualizaUpdatedAtComOTimeProviderAvancado()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var task = CriarTarefaValida(timeProvider);
        timeProvider.Advance(TimeSpan.FromMinutes(10));

        // Act
        var resultado = task.SoftDelete(timeProvider);

        // Assert
        resultado.IsSuccess.Should().BeTrue();
        task.DeletedAt.Should().Be(timeProvider.GetUtcNow().UtcDateTime);
        task.UpdatedAt.Should().Be(timeProvider.GetUtcNow().UtcDateTime);
    }

    [Fact] // CA-15
    public void Complete_OperacaoQueFalha_NaoAlteraUpdatedAt()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var task = CriarTarefaValida(timeProvider);
        task.Complete(timeProvider);
        var updatedAtAposConcluir = task.UpdatedAt;
        timeProvider.Advance(TimeSpan.FromMinutes(10));

        // Act
        var resultado = task.Complete(timeProvider);

        // Assert
        resultado.IsFailure.Should().BeTrue();
        task.UpdatedAt.Should().Be(updatedAtAposConcluir, "uma operação que falha não é uma mutação bem-sucedida (CA-15)");
    }

    [Fact] // CA-15
    public void Reopen_OperacaoQueFalha_NaoAlteraUpdatedAt()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var task = CriarTarefaValida(timeProvider);
        var updatedAtNaCriacao = task.UpdatedAt;
        timeProvider.Advance(TimeSpan.FromMinutes(10));

        // Act
        var resultado = task.Reopen(timeProvider);

        // Assert
        resultado.IsFailure.Should().BeTrue();
        task.UpdatedAt.Should().Be(updatedAtNaCriacao);
    }

    // ---------------------------------------------------------------
    // CA-16: IsOverdue
    // ---------------------------------------------------------------

    [Fact] // CA-16
    public void IsOverdue_SemDueDate_RetornaFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var task = CriarTarefaValida(timeProvider, dueDate: null);

        // Act
        var atrasada = task.IsOverdue(new DateOnly(2026, 6, 1));

        // Assert
        atrasada.Should().BeFalse();
    }

    [Fact] // CA-16
    public void IsOverdue_DueDateIgualAHoje_RetornaFalse()
    {
        // Arrange
        var hoje = new DateOnly(2026, 6, 1);
        var timeProvider = new FakeTimeProvider();
        var task = CriarTarefaValida(timeProvider, dueDate: hoje);

        // Act
        var atrasada = task.IsOverdue(hoje);

        // Assert
        atrasada.Should().BeFalse("RN-TASK-16 exige vencimento ANTERIOR a hoje, não igual");
    }

    [Fact] // CA-16
    public void IsOverdue_DueDatePassadaMasStatusCompleted_RetornaFalse()
    {
        // Arrange
        var hoje = new DateOnly(2026, 6, 1);
        var timeProvider = new FakeTimeProvider();
        var task = CriarTarefaValida(timeProvider, dueDate: hoje.AddDays(-10));
        task.Complete(timeProvider);

        // Act
        var atrasada = task.IsOverdue(hoje);

        // Assert
        atrasada.Should().BeFalse("atrasada só se aplica a tarefa Pending (RN-TASK-16)");
    }

    [Fact] // CA-16
    public void IsOverdue_PendingEDueDateAnteriorAHoje_RetornaTrue()
    {
        // Arrange
        var hoje = new DateOnly(2026, 6, 1);
        var timeProvider = new FakeTimeProvider();
        var task = CriarTarefaValida(timeProvider, dueDate: hoje.AddDays(-1));

        // Act
        var atrasada = task.IsOverdue(hoje);

        // Assert
        atrasada.Should().BeTrue();
    }

    // ---------------------------------------------------------------
    // CA-21: sem setter público
    // ---------------------------------------------------------------

    [Fact] // CA-21
    public void TodoTask_NaoExpoeSetterPublico_TodoEstadoMudaPorMetodoDeDominio()
    {
        // Arrange & Act
        var propriedadesComSetterPublico = typeof(TodoTask)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(propriedade => propriedade.GetSetMethod(nonPublic: false) is not null)
            .Select(propriedade => propriedade.Name)
            .ToList();

        // Assert
        propriedadesComSetterPublico.Should().BeEmpty(
            "todo estado de TodoTask muda só por método de domínio (Create/UpdateDetails/Complete/Reopen/SoftDelete) — "
            + "CreatedAt/UpdatedAt/DeletedAt só são graváveis via IAuditable/ISoftDeletable (implementação explícita)");
    }

    // ---------------------------------------------------------------
    // Cobertura adicional: validações de UpdateDetails e SoftDelete
    // idempotente, não amarradas a um único CA, mas parte do mesmo
    // conjunto de invariantes.
    // ---------------------------------------------------------------

    [Fact]
    public void UpdateDetails_TituloInvalido_RetornaFalhaSemAlterarONada()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var task = CriarTarefaValida(timeProvider);
        var tituloOriginal = task.Title;

        // Act
        var resultado = task.UpdateDetails("   ", null, TaskPriority.Low, null, timeProvider);

        // Assert
        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(TodoTaskErrors.TitleRequired);
        task.Title.Should().Be(tituloOriginal);
    }

    [Fact]
    public void UpdateDetails_PrioridadeForaDoIntervaloDoEnum_RetornaFalha()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var task = CriarTarefaValida(timeProvider);

        // Act
        var resultado = task.UpdateDetails("Título válido", null, (TaskPriority)99, null, timeProvider);

        // Assert
        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(TodoTaskErrors.InvalidPriority);
    }

    [Fact]
    public void SoftDelete_TarefaJaRemovida_RetornaFalha()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var task = CriarTarefaValida(timeProvider);
        task.SoftDelete(timeProvider);

        // Act
        var resultado = task.SoftDelete(timeProvider);

        // Assert
        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(TodoTaskErrors.AlreadyDeleted);
    }

    private static TodoTask CriarTarefaValida(TimeProvider timeProvider, DateOnly? dueDate = null) =>
        TodoTask.Create(_ownerId, "Tarefa válida", "Descrição válida", TaskPriority.Medium, dueDate, timeProvider).Value;
}
