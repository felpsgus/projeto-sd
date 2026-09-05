using Testcontainers.PostgreSql;
using Xunit;

namespace TodoList.Tasks.IntegrationTests.Persistence;

/// <summary>
/// Container Postgres compartilhado pelos testes da coleção "Postgres" (CA-09
/// de BE-02) — sobe uma vez por execução da coleção, não por teste.
/// <b>Requer Docker.</b> Ver <see cref="PostgresPersistenceTests"/> e o README
/// para como excluir esta categoria num ambiente sem Docker.
///
/// <para>
/// De propósito, <b>não</b> inicia o container em <c>InitializeAsync</c>: como
/// fixture de coleção, esse método roda mesmo que todo teste da coleção esteja
/// filtrado fora (<c>--filter "Category!=Docker"</c>) — tentaria falar com o
/// daemon Docker à toa numa máquina que não tem Docker. Em vez disso,
/// <see cref="EnsureStartedAsync"/> só é chamado de dentro do corpo de um
/// teste, que só executa quando o filtro deixa.
/// </para>
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("Chame EnsureStartedAsync() antes de usar o container.");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public async Task EnsureStartedAsync()
    {
        if (_container is not null)
        {
            return;
        }

        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("todolist")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _container.StartAsync();
    }
}

/// <summary>
/// Coleção xUnit (CA-09): todos os testes marcados <c>[Collection("Postgres")]</c>
/// compartilham o mesmo container — ele sobe uma única vez para a coleção
/// inteira, não um container por teste.
/// </summary>
[CollectionDefinition("Postgres")]
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresContainerFixture>
{
}
