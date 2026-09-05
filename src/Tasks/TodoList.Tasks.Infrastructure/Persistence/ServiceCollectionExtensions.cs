using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Infrastructure.Persistence.Interceptors;

namespace TodoList.Tasks.Infrastructure.Persistence;

/// <summary>
/// Fiação da persistência do Tasks Service (BE-02): <see cref="TasksDbContext"/>
/// + interceptor de auditoria/soft delete + <see cref="IUnitOfWork"/> + readiness
/// check de banco. Chamado a partir de <c>Program.cs</c> — nenhum outro lugar
/// da Api referencia <c>Microsoft.EntityFrameworkCore</c> diretamente.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Nome da seção <c>ConnectionStrings</c> lida em configuração. Nunca
    /// versionada (CA-11): em dev, <c>dotnet user-secrets set
    /// "ConnectionStrings:TasksDb" "..."</c>; no CI, a variável de ambiente
    /// <c>ConnectionStrings__TasksDb</c>. Ver README.
    /// </summary>
    public const string ConnectionStringName = "TasksDb";

    public static IServiceCollection AddTasksPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        // TryAdd: se o host (Program.cs ou um WebApplicationFactory de teste)
        // já registrou um TimeProvider — por exemplo um FakeTimeProvider —,
        // esta chamada não o substitui.
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<AuditingSaveChangesInterceptor>();

        services.AddDbContext<TasksDbContext>((serviceProvider, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' não configurada. Configure com "
                    + $"'dotnet user-secrets set \"ConnectionStrings:{ConnectionStringName}\" \"...\"' em desenvolvimento, "
                    + $"ou a variável de ambiente 'ConnectionStrings__{ConnectionStringName}' no CI. Ver README.");
            }

            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(TasksDbContext.MigrationsHistoryTableName, TasksDbContext.Schema));

            // snake_case em toda tabela/coluna (nota técnica de BE-02) — ver
            // comentário do pacote EFCore.NamingConventions no .csproj.
            options.UseSnakeCaseNamingConvention();

            options.AddInterceptors(serviceProvider.GetRequiredService<AuditingSaveChangesInterceptor>());
        });

        services.AddScoped<IUnitOfWork>(serviceProvider => serviceProvider.GetRequiredService<TasksDbContext>());

        // BE-05: repositório de TodoTask — implementação concreta contra o
        // TasksDbContext; a Application só conhece ITodoTaskRepository.
        services.AddScoped<ITodoTaskRepository, TodoTaskRepository>();

        return services;
    }

    /// <summary>
    /// Readiness check (CA-03, CA-04): tag <c>"ready"</c>, para que
    /// <c>/health/ready</c> rode só ele (liveness em <c>/health</c> não
    /// depende de banco). Banco fora do ar não derruba o processo — vira
    /// <c>Unhealthy</c> reportado aqui, nunca uma exceção não tratada, porque
    /// nenhum lugar chama <c>Migrate()</c>/<c>EnsureCreated()</c> nem resolve
    /// o <see cref="TasksDbContext"/> de forma eager no startup.
    /// </summary>
    public static IHealthChecksBuilder AddTasksDatabaseHealthCheck(this IHealthChecksBuilder builder) =>
        builder.AddDbContextCheck<TasksDbContext>("tasks-database", tags: ["ready"]);
}
