using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TodoList.Tasks.Domain.Tasks;
using TodoList.Tasks.Infrastructure.Persistence;
using Xunit;

namespace TodoList.Tasks.UnitTests;

/// <summary>
/// CA-13 de BE-02 e CA-05/CA-17/CA-20 de BE-05: inspeciona o <c>Model</c> do
/// EF do <see cref="TasksDbContext"/> — não é revisão manual. Construir o
/// contexto aqui não conecta a nenhum banco: montar o <c>Model</c> é só
/// reflexão sobre o próprio <c>OnModelCreating</c>, então a connection string
/// abaixo nunca chega a ser usada de verdade (nem precisa ser válida).
/// </summary>
public class PersistenceModelTests
{
    [Fact] // CA-13
    public void TasksDbContext_TemSchemaPadraoTasks()
    {
        using var context = CreateContext();

        context.Model.GetDefaultSchema().Should().Be("tasks");
    }

    [Fact] // CA-13
    public void TasksDbContext_NaoMapeiaNenhumaEntidadeNoSchemaIdentity()
    {
        using var context = CreateContext();

        context.Model.GetEntityTypes()
            .Should()
            .NotContain(
                entityType => entityType.GetSchema() == "identity",
                "o TasksDbContext não pode conhecer nada do schema do Identity (CA-13, D-27) — quem precisa saber de um usuário pergunta por gRPC");
    }

    [Fact] // (a) da tarefa BE-05: OwnerId é coluna simples, nunca FK de navegação
    public void TodoTask_OwnerId_NaoTemNenhumaChaveEstrangeiraNoModeloDoEf()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(TodoTask))!;

        entityType.GetForeignKeys().Should().BeEmpty(
            "OwnerId é só uma coluna Guid — nenhuma FK de navegação para User pode existir no modelo do EF "
            + "(D-27/CA-13 de BE-02); a FK real, cruzando schemas, é SQL explícito fora do modelo (BE-02 CA-02c)");

        entityType.GetNavigations().Should().BeEmpty("sem propriedade de navegação para User");
    }

    [Fact] // CA-17
    public void TodoTask_IsOverdue_NaoEhPropriedadeMapeadaNoModelo()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(TodoTask))!;

        entityType.GetProperties()
            .Select(property => property.Name)
            .Should()
            .NotContain("IsOverdue", "IsOverdue é derivado em memória (RN-TASK-16) — nunca uma coluna persistida");
    }

    [Fact] // CA-20
    public void TodoTask_TemIndiceComposto_OwnerIdEStatus()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(TodoTask))!;
        var colunasEsperadas = new[] { "OwnerId", "Status" };

        entityType.GetIndexes()
            .Should()
            .Contain(index => index.Properties.Select(p => p.Name).SequenceEqual(colunasEsperadas));
    }

    [Fact] // CA-20
    public void TodoTask_TemIndiceComposto_OwnerIdEDueDate()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(TodoTask))!;
        var colunasEsperadas = new[] { "OwnerId", "DueDate" };

        entityType.GetIndexes()
            .Should()
            .Contain(index => index.Properties.Select(p => p.Name).SequenceEqual(colunasEsperadas));
    }

    private static TasksDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused;Password=unused")
            .Options;

        return new TasksDbContext(options);
    }
}
