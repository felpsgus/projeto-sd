using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoList.Tasks.Domain.Tasks;

namespace TodoList.Tasks.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeamento EF de <see cref="TodoTask"/> (BE-05). Uma classe
/// <see cref="IEntityTypeConfiguration{TEntity}"/> por entidade, aplicada via
/// <c>ApplyConfigurationsFromAssembly</c> em <see cref="TasksDbContext"/>
/// (BE-02).
/// </summary>
public sealed class TodoTaskConfiguration : IEntityTypeConfiguration<TodoTask>
{
    public void Configure(EntityTypeBuilder<TodoTask> builder)
    {
        // Nome explícito da tabela: "tasks.tasks" (schema default já é
        // "tasks", ver TasksDbContext) — o mesmo nome que BE-02 documenta.
        builder.ToTable("tasks");

        builder.HasKey(task => task.Id);

        // OwnerId é só uma coluna Guid — de propósito, sem HasOne/navegação
        // para User. Ver o comentário longo em TodoTask.OwnerId: mapear isso
        // como FK de navegação exigiria o TasksDbContext conhecer a entidade
        // User do schema identity, o que quebra D-27/CA-13 de BE-02 (o teste
        // PersistenceModelTests.TasksDbContext_NaoMapeiaNenhumaEntidadeNoSchemaIdentity
        // reprovaria). A FK real, cruzando schemas, é SQL explícito numa
        // migration à parte (BE-02 CA-02c) — não gerada a partir deste
        // mapeamento.
        builder.Property(task => task.OwnerId)
            .IsRequired();

        builder.Property(task => task.Title)
            .IsRequired()
            .HasMaxLength(TodoTask.TitleMaxLength);

        builder.Property(task => task.Description)
            .HasMaxLength(TodoTask.DescriptionMaxLength);

        // Persistidos como int (BE-05, nota técnica) — HasConversion<int>()
        // aqui é redundante com o comportamento padrão do EF para enum (já
        // grava o valor numérico subjacente), mas explícito documenta a
        // decisão e blinda contra qualquer convenção futura que mude o
        // padrão.
        builder.Property(task => task.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(task => task.Priority)
            .IsRequired()
            .HasConversion<int>();

        // DateOnly mapeia nativamente para "date" no Postgres via Npgsql —
        // sem hora, sem fuso (D-18: "hoje" é decidido na borda, não aqui).
        builder.Property(task => task.DueDate);

        // DateTime?/DateTime: passam pela conversão UTC global
        // (EfConventions.ConfigureUtcDateTime, aplicada a Properties<DateTime>()
        // — alcança também as anuláveis, ver o comentário lá).
        builder.Property(task => task.CompletedAt);
        builder.Property(task => task.CreatedAt);
        builder.Property(task => task.UpdatedAt);
        builder.Property(task => task.DeletedAt);

        // CA-20: índices que sustentam as consultas de listagem/atraso do
        // BE-22 (filtrar por status e por vencimento, sempre escopado ao dono).
        builder.HasIndex(task => new { task.OwnerId, task.Status });
        builder.HasIndex(task => new { task.OwnerId, task.DueDate });

        // Sem HasQueryFilter aqui: o filtro global de soft delete já é
        // aplicado a toda entidade ISoftDeletable por
        // EfConventions.ApplySoftDeleteQueryFilters, chamado em
        // TasksDbContext.OnModelCreating.
    }
}
