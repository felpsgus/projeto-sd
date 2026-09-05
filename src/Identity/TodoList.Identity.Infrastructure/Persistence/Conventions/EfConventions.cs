using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TodoList.Identity.Domain.Common;

namespace TodoList.Identity.Infrastructure.Persistence.Conventions;

/// <summary>
/// Convenções globais de mapeamento do BE-02 (DateTime em UTC, soft delete via
/// <c>HasQueryFilter</c>, string sem <c>MaxLength</c> proibido). Extraídas em
/// métodos estáticos — não só chamadas dentro do <c>OnModelCreating</c> de
/// <see cref="IdentityDbContext"/> — para que
/// <c>TodoList.Identity.IntegrationTests</c> monte um <c>DbContext</c> de
/// teste (SQLite) que reaproveita o mecanismo real, e não uma cópia dele
/// (pedido explícito de BE-02).
/// </summary>
public static class EfConventions
{
    /// <summary>
    /// Todo <see cref="DateTime"/> e <see cref="DateTime"/>? do modelo passa pela
    /// conversão para UTC.
    ///
    /// <para>
    /// <b>Um registro só — ele cobre as duas formas.</b> <c>Properties&lt;DateTime&gt;()</c>
    /// alcança também as propriedades <c>DateTime?</c>: o EF envolve o conversor
    /// para o anulável sozinho. Registrar um segundo conversor em
    /// <c>Properties&lt;DateTime?&gt;()</c> funciona, mas passa a exigir dois
    /// conversores mantidos em sincronia para sempre, sem ganho nenhum — e no dia
    /// em que divergirem, data anulável e não-anulável passam a ser gravadas por
    /// regras diferentes. Travado por ConvencoesDeModeloTests.
    /// </para>
    /// </summary>
    public static void ConfigureUtcDateTime(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    /// <summary>
    /// CA-06: toda entidade que implementa <see cref="ISoftDeletable"/> ganha
    /// um filtro global excluindo <c>DeletedAt != null</c> das consultas
    /// normais. <c>IgnoreQueryFilters()</c> continua disponível como caminho
    /// explícito (usado só pelo expurgo, BE-23).
    /// </summary>
    public static void ApplySoftDeleteQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "entity");
            var deletedAtProperty = Expression.Property(parameter, nameof(ISoftDeletable.DeletedAt));
            var isNotDeleted = Expression.Equal(deletedAtProperty, Expression.Constant(null, typeof(DateTime?)));
            var lambda = Expression.Lambda(isNotDeleted, parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }

    /// <summary>
    /// Nota técnica de BE-02: "string sem MaxLength explícito é proibido — cada
    /// propriedade declara o seu limite". Em vez de depender de revisão,
    /// falha a construção do modelo (portanto o startup do serviço, e o build
    /// da migration) se alguma propriedade string ficou sem limite.
    /// </summary>
    public static void RequireExplicitMaxLengthOnStrings(ModelBuilder modelBuilder)
    {
        var semLimite = modelBuilder.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties(), (entityType, property) => (entityType, property))
            .Where(pair => pair.property.ClrType == typeof(string) && pair.property.GetMaxLength() is null)
            .Select(pair => $"{pair.entityType.ClrType.Name}.{pair.property.Name}")
            .ToList();

        if (semLimite.Count > 0)
        {
            throw new InvalidOperationException(
                "Propriedades string sem MaxLength explícito (CONVENCOES-CODIGO.md, BE-02): "
                + string.Join(", ", semLimite)
                + ". Configure HasMaxLength em um IEntityTypeConfiguration<T>.");
        }
    }
}
