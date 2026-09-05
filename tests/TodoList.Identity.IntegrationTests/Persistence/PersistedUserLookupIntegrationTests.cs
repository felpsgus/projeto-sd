using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Identity.Infrastructure.Users;
using Xunit;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// <see cref="PersistedUserLookup"/> sobre Postgres real (BE-26, CA-13):
/// desativar um usuário no banco muda a resposta de <c>active=true</c> para
/// <c>active=false</c> <b>sem reiniciar o serviço</b> — cada chamada usa um
/// <see cref="UserRepository"/>/<see cref="IdentityDbContext"/> novo (o
/// equivalente a um novo escopo por requisição), nunca reaproveitando estado
/// entre elas, provando que não há cache escondido. <b>Requer Docker.</b>
/// </summary>
[Collection("Postgres")]
public class PersistedUserLookupIntegrationTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;

    public PersistedUserLookupIntegrationTests(PostgresContainerFixture fixture)
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
    public async Task FindByIdAsync_AposDesativarUsuarioNoBanco_RefleteSemReiniciarOServico() // CA-13 de BE-26
    {
        var timeProvider = TimeProvider.System;
        Guid userId;

        await using (var context = CreateContext())
        {
            var user = User.Create(Email.Create("reflete@exemplo.com").Value, "Usuário Refletido", "hash", timeProvider).Value;
            context.Users.Add(user);
            await context.SaveChangesAsync();
            userId = user.Id;
        }

        await using (var context = CreateContext())
        {
            var lookup = new PersistedUserLookup(new UserRepository(context));

            var antes = await lookup.FindByIdAsync(userId, CancellationToken.None);

            antes.Should().NotBeNull();
            antes!.Active.Should().BeTrue();
        }

        await using (var context = CreateContext())
        {
            var user = await context.Users.SingleAsync(u => u.Id == userId);
            user.Deactivate(timeProvider);
            await context.SaveChangesAsync();
        }

        // Instância nova de PersistedUserLookup, sobre um DbContext novo — nada
        // reaproveitado da chamada "antes" acima.
        await using (var context = CreateContext())
        {
            var lookup = new PersistedUserLookup(new UserRepository(context));

            var depois = await lookup.FindByIdAsync(userId, CancellationToken.None);

            depois.Should().NotBeNull();
            depois!.Active.Should().BeFalse(
                "desativar o usuário no banco precisa refletir na próxima chamada, sem reiniciar o serviço (CA-13 de BE-26)");
        }
    }

    [Fact]
    [Trait("Category", "Docker")]
    public async Task FindByIdAsync_UsuarioInexistenteNoBanco_RetornaNull()
    {
        await using var context = CreateContext();
        var lookup = new PersistedUserLookup(new UserRepository(context));

        var result = await lookup.FindByIdAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

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
