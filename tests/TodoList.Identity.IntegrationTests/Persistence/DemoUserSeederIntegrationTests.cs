using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Identity.Infrastructure.Users;
using Xunit;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// <see cref="DemoUserSeeder"/> contra Postgres real — confirma que os dois
/// usuários de demonstração são persistidos de verdade com os ids fixos do
/// <see cref="InMemoryUserLookup"/> (necessário para a FK cruzada
/// <c>tasks.tasks.owner_id → identity.users(id)</c>, nota do README) e que
/// rodar a seeder duas vezes seguidas contra o mesmo banco não duplica nem
/// falha — inclusive sob a garantia real do índice único de e-mail, não só a
/// checagem em memória. <b>Requer Docker.</b>
/// </summary>
[Collection("Postgres")]
public class DemoUserSeederIntegrationTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;

    public DemoUserSeederIntegrationTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.EnsureStartedAsync();

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("Category", "Docker")]
    public async Task SeedAsync_ChamadoDuasVezesContraOMesmoBanco_CriaExatamenteDoisUsuariosSemFalhar()
    {
        await using (var context = CreateContext())
        {
            var seeder = CreateSeeder(context);
            await seeder.SeedAsync(CancellationToken.None);
        }

        await using (var context = CreateContext())
        {
            var seeder = CreateSeeder(context);
            var act = async () => await seeder.SeedAsync(CancellationToken.None);

            await act.Should().NotThrowAsync("rodar o seed duas vezes não pode duplicar nem falhar");
        }

        await using var verificacao = CreateContext();
        var total = await verificacao.Users.CountAsync();
        total.Should().Be(2);

        var ativo = await verificacao.Users.SingleAsync(u => u.Id == InMemoryUserLookup.ActiveUserId);
        ativo.IsActive.Should().BeTrue();

        var inativo = await verificacao.Users.SingleAsync(u => u.Id == InMemoryUserLookup.InactiveUserId);
        inativo.IsActive.Should().BeFalse();
    }

    private static DemoUserSeeder CreateSeeder(IdentityDbContext context) =>
        new(new UserRepository(context), context, TimeProvider.System, NullLogger<DemoUserSeeder>.Instance);

    private IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                _fixture.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable(IdentityDbContext.MigrationsHistoryTableName, IdentityDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new IdentityDbContext(options);
    }
}
