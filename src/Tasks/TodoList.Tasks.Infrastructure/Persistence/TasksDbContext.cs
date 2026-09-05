using Microsoft.EntityFrameworkCore;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Domain.Tasks;
using TodoList.Tasks.Infrastructure.Persistence.Conventions;

namespace TodoList.Tasks.Infrastructure.Persistence;

/// <summary>
/// Contexto EF Core do Tasks Service (BE-02, D-27). Schema fixo
/// <see cref="Schema"/> — mapeia só a tabela do próprio serviço (hoje,
/// <see cref="Tasks"/>/<c>tasks.tasks</c>, BE-05); nunca um <c>DbSet</c> para
/// entidade do Identity (CA-13).
///
/// <para>
/// <b>FK cruzada entre schemas (BE-02 CA-02c/CA-15/CA-16):</b>
/// <c>tasks.tasks.owner_id → identity.users(id) ON DELETE CASCADE</c> (D-27)
/// existe desde a migration <c>AddOwnerForeignKeyToIdentityUsers</c>, por
/// <b>SQL explícito</b> (<c>migrationBuilder.Sql(...)</c>) — o
/// <see cref="TasksDbContext"/> continua sem mapear <c>User</c>, a entidade
/// do outro lado (ver o comentário em <see cref="TodoTask.OwnerId"/>), então
/// o EF não tinha como gerar essa FK a partir do modelo. Essa FK é o que
/// sustenta a exclusão em cascata de BE-16,
/// inclusive das tarefas soft-deleted (CA-16 de BE-02) — comprovado por
/// consulta ao catálogo do Postgres (<c>pg_constraint</c>), não só pelo código.
/// </para>
///
/// <para>
/// <b>Consequência: a migration do Tasks agora exige o schema `identity` já
/// aplicado.</b> A migration que cria essa FK referencia <c>identity.users</c>
/// — rodá-la contra um banco onde o Identity ainda não foi migrado falha com
/// <c>ERRO: esquema "identity" não existe</c> (SQLSTATE <c>3F000</c>, confirmado
/// contra Postgres real). Por isso a ordem "Identity primeiro" (README) deixou de ser só uma
/// recomendação de organização e passou a ser um requisito rígido: qualquer
/// código/teste que aplica migrations do Tasks contra um banco vazio precisa
/// migrar o <c>IdentityDbContext</c> antes.
/// </para>
///
/// <para>
/// Implementa <see cref="IUnitOfWork"/> diretamente: a assinatura de
/// <c>DbContext.SaveChangesAsync(CancellationToken)</c> já é exatamente a que
/// a Application precisa (CA-12), então não há necessidade de um wrapper à
/// parte.
/// </para>
/// </summary>
public sealed class TasksDbContext : DbContext, IUnitOfWork
{
    /// <summary>Schema Postgres deste serviço (D-27) — nunca "identity".</summary>
    public const string Schema = "tasks";

    /// <summary>
    /// Tabela de histórico de migrations própria deste serviço. Sem isso os
    /// dois serviços disputariam a mesma <c>__EFMigrationsHistory</c> e um
    /// apagaria a migration do outro (nota técnica de BE-02).
    /// </summary>
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public TasksDbContext(DbContextOptions<TasksDbContext> options)
        : base(options)
    {
    }

    /// <summary>BE-05: única tabela deste serviço. Mapeamento em <c>Configurations/TodoTaskConfiguration.cs</c>.</summary>
    public DbSet<TodoTask> Tasks => Set<TodoTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        // Uma classe IEntityTypeConfiguration<T> por entidade (BE-02) —
        // TodoTaskConfiguration (BE-05) é aplicada por aqui.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TasksInfrastructureAssemblyMarker).Assembly);

        EfConventions.ApplySoftDeleteQueryFilters(modelBuilder);
        EfConventions.RequireExplicitMaxLengthOnStrings(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        EfConventions.ConfigureUtcDateTime(configurationBuilder);
    }
}
