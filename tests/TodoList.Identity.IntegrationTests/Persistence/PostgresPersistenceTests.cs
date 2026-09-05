using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Identity.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// Testes de integração reais contra Postgres via Testcontainers (CA-05,
/// CA-02b de BE-02). <b>Requer Docker.</b> Rodam por padrão — são eles que
/// provam que a persistência funciona contra Postgres de verdade, e não só
/// sobre o SQLite in-memory dos outros testes desta pasta. Num ambiente sem
/// Docker, exclua a categoria explicitamente:
/// <c>dotnet test --filter "Category!=Docker"</c>.
///
/// <para>
/// CA-10 (independência entre testes): cada teste começa de um estado limpo
/// (<see cref="InitializeAsync"/> recria a tabela de teste) mesmo os testes
/// da coleção compartilhando um único container Postgres
/// (<see cref="PostgresContainerFixture"/>, subido uma vez por
/// <see cref="PostgresCollectionDefinition"/>) — evita o custo de subir um container
/// por teste sem deixar estado vazar de um teste para o outro.
/// </para>
/// </summary>
[Collection("Postgres")]
public class PostgresPersistenceTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private FakeTimeProvider _timeProvider = null!;

    public PostgresPersistenceTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        // Chamado daqui, e não do InitializeAsync da fixture de coleção: aquele
        // roda mesmo quando todo teste da coleção está filtrado fora
        // (--filter "Category!=Docker") e tentaria falar com o daemon Docker à toa.
        await _fixture.EnsureStartedAsync();

        await using var context = CreateProbeContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("Category", "Docker")]
    public async Task SaveChangesAsync_ComPostgresReal_DateTimeNaoUtcVoltaConvertidoSemDeslocamento() // CA-05
    {
        // Kind=Local de propósito: é o caso que a convenção UTC existe para
        // resolver. Um valor que já nasce Kind=Utc (CreatedAt/UpdatedAt, vindos
        // do TimeProvider) volta correto do Npgsql mesmo com a convenção
        // desligada — asserção só sobre ele não provaria nada.
        var instante = new DateTimeOffset(2026, 3, 15, 9, 30, 0, TimeSpan.FromHours(-3));
        var horarioLocal = instante.LocalDateTime;

        horarioLocal.Kind.Should().Be(
            DateTimeKind.Local,
            "o teste só vale se o que entra não for UTC — é a conversão que está sob teste");

        await using var context = CreateProbeContext();

        var probe = new AuditableProbe { Name = "utc-roundtrip", OccurredAt = horarioLocal };
        context.Probes.Add(probe);
        await context.SaveChangesAsync();
        var idSalvo = probe.Id;

        // Novo contexto (nova conexão/leitura), para garantir que o valor
        // volta do Postgres — não do change tracker em memória.
        await using var novoContexto = CreateProbeContext();
        var lido = await novoContexto.Probes.SingleAsync(probe => probe.Id == idSalvo);

        lido.OccurredAt.Kind.Should().Be(DateTimeKind.Utc, "tudo que sai do banco é UTC (CA-05)");
        lido.OccurredAt.Should().Be(
            instante.UtcDateTime,
            "o instante precisa sobreviver ao round-trip convertido para UTC, sem deslocamento");

        // O caminho do interceptor de auditoria continua coberto.
        lido.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        lido.CreatedAt.Should().Be(_timeProvider.GetUtcNow().UtcDateTime);
    }

    [Fact]
    [Trait("Category", "Docker")]
    public async Task MigrateAsync_AplicadoDuasVezesEmBancoVazio_EhNoOpSemErro() // CA-02b
    {
        await using var context = CreateIdentityContext();

        await context.Database.MigrateAsync();

        var aplicandoDeNovo = async () => await context.Database.MigrateAsync();

        await aplicandoDeNovo.Should().NotThrowAsync(
            "rodar as migrations de novo num banco já migrado precisa ser no-op, sem erro (CA-02b)");
    }

    private ProbeDbContext CreateProbeContext()
    {
        var interceptor = new AuditingSaveChangesInterceptor(_timeProvider);

        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        return new ProbeDbContext(options);
    }

    private IdentityDbContext CreateIdentityContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                _fixture.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable(IdentityDbContext.MigrationsHistoryTableName, IdentityDbContext.Schema))
            // BE-04: com User mapeado, o modelo em runtime precisa da mesma
            // convenção snake_case usada em produção (ServiceCollectionExtensions)
            // e no design-time (IdentityDbContextFactory) — senão o modelo
            // calculado aqui diverge do snapshot da migration real (nomes de
            // tabela/coluna diferentes) e o EF acusa "pending model changes"
            // mesmo sem nenhuma mudança de verdade.
            .UseSnakeCaseNamingConvention()
            .Options;

        return new IdentityDbContext(options);
    }
}
