using Microsoft.EntityFrameworkCore;
using TodoList.Identity.Infrastructure.Persistence.Conventions;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// <c>DbContext</c> só deste projeto de teste, mapeando só
/// <see cref="AuditableProbe"/> — mas chamando as mesmas convenções estáticas
/// de <see cref="EfConventions"/> usadas por <c>IdentityDbContext</c> (BE-02):
/// UTC obrigatório, soft delete via <c>HasQueryFilter</c> e a checagem de
/// <c>MaxLength</c> explícito. O interceptor de auditoria/soft delete
/// (<c>AuditingSaveChangesInterceptor</c>) também é o real, injetado via
/// <c>AddInterceptors</c> na criação das <c>DbContextOptions</c> — não uma
/// cópia. O objetivo é testar o mecanismo real da Infrastructure sem
/// depender de Postgres/Docker (rodando sobre SQLite in-memory).
/// </summary>
public sealed class ProbeDbContext : DbContext
{
    public ProbeDbContext(DbContextOptions<ProbeDbContext> options)
        : base(options)
    {
    }

    public DbSet<AuditableProbe> Probes => Set<AuditableProbe>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Único lugar deste contexto de teste com configuração "manual" (não
        // há assembly de IEntityTypeConfiguration<T> aqui, só uma entidade).
        modelBuilder.Entity<AuditableProbe>(entity => entity.Property(probe => probe.Name).HasMaxLength(200));

        EfConventions.ApplySoftDeleteQueryFilters(modelBuilder);
        EfConventions.RequireExplicitMaxLengthOnStrings(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        EfConventions.ConfigureUtcDateTime(configurationBuilder);
    }
}
