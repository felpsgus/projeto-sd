using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TodoList.Tasks.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace TodoList.Tasks.IntegrationTests.Persistence;

/// <summary>
/// CA-06, CA-07, CA-08 de BE-02 — CRUD trivial da entidade de teste
/// (<see cref="AuditableProbe"/>) exercitando o pipeline relacional real do
/// EF Core (<c>SaveChanges</c>, interceptor, query filter) sobre SQLite
/// in-memory. Não precisa de Docker/Postgres: é exatamente o que o escopo
/// desta rodada pede para "tudo que puder ser verificado sem Postgres".
/// CA-05 (round-trip real de <c>timestamptz</c>) fica só nos testes
/// Testcontainers marcados como skip (<see cref="PostgresPersistenceTests"/>),
/// porque depende do tipo de coluna real do Postgres.
///
/// <para>
/// <c>EnsureCreated()</c> aparece aqui só para o banco SQLite in-memory
/// descartável deste teste — não contradiz a proibição de
/// <c>EnsureCreated</c>/<c>Migrate()</c> automático de BE-02, que é sobre o
/// app real (que usa migrations versionadas contra Postgres).
/// </para>
/// </summary>
public class AuditingAndSoftDeleteTests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _connection = null!;
    private FakeTimeProvider _timeProvider = null!;

    public Task InitializeAsync()
    {
        // "DataSource=:memory:" cria um banco novo por conexão; mantê-la
        // aberta pelo tempo de vida do teste é o que faz o banco sobreviver
        // entre os vários DbContext criados dentro do mesmo teste.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // CA1001: o tipo tem um campo descartável (_connection), então precisa
    // ser IDisposable — mas a disposição de verdade acontece em
    // DisposeAsync (IAsyncLifetime, chamado pelo xUnit ao fim de cada teste).
    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-07
    public async Task SaveChangesAsync_AoInserir_PreencheCreatedAtEUpdatedAt()
    {
        await using var context = CreateContext();

        var probe = new AuditableProbe { Name = "primeira" };
        context.Probes.Add(probe);
        await context.SaveChangesAsync();

        var esperado = _timeProvider.GetUtcNow().UtcDateTime;
        probe.CreatedAt.Should().Be(esperado);
        probe.UpdatedAt.Should().Be(esperado);
    }

    [Fact] // CA-07, CA-08
    public async Task SaveChangesAsync_AoAlterar_SoUpdatedAtMuda_AvancandoOFakeTimeProviderSemThreadSleep()
    {
        await using var context = CreateContext();

        var probe = new AuditableProbe { Name = "original" };
        context.Probes.Add(probe);
        await context.SaveChangesAsync();
        var createdAtOriginal = probe.CreatedAt;

        // CA-08: avança o relógio fake — nada de Thread.Sleep — e confirma
        // que o próximo SaveChanges usa esse novo instante.
        _timeProvider.Advance(TimeSpan.FromMinutes(10));

        probe.Name = "alterado";
        await context.SaveChangesAsync();

        probe.CreatedAt.Should().Be(createdAtOriginal, "CreatedAt não muda numa alteração (CA-07)");
        probe.UpdatedAt.Should().Be(
            _timeProvider.GetUtcNow().UtcDateTime,
            "UpdatedAt reflete o tempo avançado do TimeProvider injetado (CA-08)");
    }

    [Fact] // CA-06
    public async Task Remove_ConvertePararRemocaoLogica_ESomeDaConsultaNormalMasApareceComIgnoreQueryFilters()
    {
        int id;

        await using (var context = CreateContext())
        {
            var probe = new AuditableProbe { Name = "para remover" };
            context.Probes.Add(probe);
            await context.SaveChangesAsync();
            id = probe.Id;

            context.Probes.Remove(probe);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var naConsultaNormal = await context.Probes.SingleOrDefaultAsync(probe => probe.Id == id);
            naConsultaNormal.Should().BeNull("uma entidade removida não aparece na consulta normal (CA-06)");

            var comFiltroIgnorado = await context.Probes.IgnoreQueryFilters().SingleOrDefaultAsync(probe => probe.Id == id);
            comFiltroIgnorado.Should().NotBeNull("IgnoreQueryFilters() é o caminho explícito para ver removidos (CA-06)");
            comFiltroIgnorado!.DeletedAt.Should().NotBeNull();
        }
    }

    private ProbeDbContext CreateContext()
    {
        var interceptor = new AuditingSaveChangesInterceptor(_timeProvider);

        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptor)
            .Options;

        var context = new ProbeDbContext(options);
        context.Database.EnsureCreated();

        return context;
    }
}
