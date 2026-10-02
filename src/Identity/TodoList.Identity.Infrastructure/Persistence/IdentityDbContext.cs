using Microsoft.EntityFrameworkCore;
using Npgsql;
using TodoList.Identity.Application.Persistence;
using TodoList.Identity.Domain.Users;
using TodoList.SharedKernel;
using TodoList.SharedKernel.Persistence;

namespace TodoList.Identity.Infrastructure.Persistence;

/// <summary>
/// Contexto EF Core do Identity Service (BE-02, D-27). Schema fixo
/// <see cref="Schema"/> — mapeia só as tabelas do próprio serviço; nunca um
/// <c>DbSet</c> para entidade do Tasks (CA-13). <see cref="User"/> é a
/// primeira entidade de negócio mapeada (BE-04); <c>refresh_tokens</c> é
/// BE-10, <c>login_attempts</c> é BE-12.
///
/// <para>
/// Implementa <see cref="IUnitOfWork"/> diretamente: a assinatura de
/// <c>DbContext.SaveChangesAsync(CancellationToken)</c> já é exatamente a que
/// a Application precisa (CA-12), então não há necessidade de um wrapper à
/// parte.
/// </para>
/// </summary>
public sealed class IdentityDbContext : DbContext, IUnitOfWork
{
    /// <summary>Schema Postgres deste serviço (D-27) — nunca "tasks".</summary>
    public const string Schema = "identity";

    /// <summary>SQLSTATE de violação de unicidade do Postgres (RN-AUTH-02, BE-07 CA-12).</summary>
    private const string PostgresUniqueViolationSqlState = "23505";

    /// <summary>
    /// Tabela de histórico de migrations própria deste serviço. Sem isso os
    /// dois serviços disputariam a mesma <c>__EFMigrationsHistory</c> e um
    /// apagaria a migration do outro (nota técnica de BE-02).
    /// </summary>
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        // Uma classe IEntityTypeConfiguration<T> por entidade (BE-02) —
        // UserConfiguration (BE-04) é a primeira deste assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityInfrastructureAssemblyMarker).Assembly);

        EfConventions.ApplySoftDeleteQueryFilters(modelBuilder);
        EfConventions.RequireExplicitMaxLengthOnStrings(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        EfConventions.ConfigureUtcDateTime(configurationBuilder);
    }

    /// <summary>
    /// BE-07, CA-12: traduz uma violação de unicidade real do Postgres
    /// (SQLSTATE <see cref="PostgresUniqueViolationSqlState"/> — hoje, só o
    /// índice único de <c>users.email</c>, RN-AUTH-02) para
    /// <see cref="UniqueConstraintViolationException"/>, o tipo que a
    /// Application conhece (ela não referencia Npgsql/EF Core, D-27). Qualquer
    /// outro <see cref="DbUpdateException"/> continua propagando como está —
    /// só esta causa específica tem tratamento de negócio conhecido.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresUniqueViolationSqlState })
        {
            throw new UniqueConstraintViolationException("Violação de restrição de unicidade ao salvar usuário.", ex);
        }
    }
}
