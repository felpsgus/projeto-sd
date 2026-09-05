using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TodoList.Identity.Infrastructure.Persistence.Design;

/// <summary>
/// Fábrica usada só em <b>design-time</b> por <c>dotnet ef migrations add</c> /
/// <c>dotnet ef database update</c> (CA-01) — nunca em runtime, onde a
/// connection string vem de configuração
/// (<see cref="ServiceCollectionExtensions.AddIdentityPersistence"/>).
///
/// <para>
/// <b>Por que ler variável de ambiente aqui:</b> quando existe um
/// <c>IDesignTimeDbContextFactory</c>, o <c>dotnet ef</c> usa esta classe e
/// <b>ignora</b> a configuração do <c>--startup-project</c> — inclusive o
/// <c>dotnet user-secrets</c>. Sem a leitura abaixo, um
/// <c>database update</c> aplicaria as migrations no banco fixado em código,
/// não no que o desenvolvedor configurou, silenciosamente. A variável é a
/// mesma que o CI já usa (<c>ConnectionStrings__IdentityDb</c>), então não há
/// chave nova a aprender.
/// </para>
/// </summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    /// <summary>
    /// Variável de ambiente lida por este comando — mesma convenção de
    /// <c>ConnectionStrings:IdentityDb</c> em configuração (README).
    /// </summary>
    public const string ConnectionStringEnvironmentVariable = "ConnectionStrings__IdentityDb";

    // Fallback para o desenvolvedor que só subiu o docker-compose.yml da raiz
    // e não configurou nada: é exatamente a credencial local daquele arquivo,
    // não um segredo real (CA-11). Host por IP de loopback, e não pelo nome de
    // host habitual, por consistência com o TasksDbContextFactory — lá o nome
    // cairia na varredura de endereço hardcoded de BE-27 (CA-06).
    private const string LocalDevelopmentFallback =
        "Host=127.0.0.1;Port=5432;Database=todolist;Username=postgres;Password=postgres";

    public IdentityDbContext CreateDbContext(string[] args)
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        var connectionString = string.IsNullOrWhiteSpace(configured) ? LocalDevelopmentFallback : configured;

        var optionsBuilder = new DbContextOptionsBuilder<IdentityDbContext>();

        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable(IdentityDbContext.MigrationsHistoryTableName, IdentityDbContext.Schema));

        // Mesma convenção de nomenclatura usada em runtime (ServiceCollectionExtensions)
        // — sem isso, a migration gerada aqui divergiria do modelo real.
        optionsBuilder.UseSnakeCaseNamingConvention();

        return new IdentityDbContext(optionsBuilder.Options);
    }
}
