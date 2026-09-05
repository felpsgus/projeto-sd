using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TodoList.Contracts.Identity.V1;
using TodoList.Identity.Api.Configuration;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Identity.IntegrationTests;
using Xunit;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// Servidor gRPC real (<see cref="WebApplicationFactory{TEntryPoint}"/>) com
/// <c>UserStore:Provider=Persisted</c> contra Postgres real — BE-26 CA-13
/// ponta a ponta: prova que a fiação de DI do <c>Program.cs</c>
/// (<c>IUserLookup</c> Scoped por cima de <c>PersistedUserLookup</c>/
/// <c>IdentityDbContext</c> Scoped) realmente sobe sem "captive dependency" e
/// que desativar um usuário no banco muda a resposta de <c>ValidateUser</c>
/// sem reiniciar o processo. <b>Requer Docker.</b>
/// </summary>
[Collection("Postgres")]
public class ValidateUserGrpcComProviderPersistidoTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;

    public ValidateUserGrpcComProviderPersistidoTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.EnsureStartedAsync();

        await using (var context = CreateContext())
        {
            await context.Database.EnsureDeletedAsync();
            await context.Database.MigrateAsync();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"ConnectionStrings:{ServiceCollectionExtensions.ConnectionStringName}"] = _fixture.ConnectionString,
                    [$"{UserStoreOptions.SectionName}:Provider"] = UserStoreOptions.PersistedProvider,
                })));
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Docker")]
    public async Task ValidateUser_ComProviderPersistido_RefleteDesativacaoNoBancoSemReiniciarOServico() // CA-13 de BE-26
    {
        var timeProvider = TimeProvider.System;
        Guid userId;

        await using (var context = CreateContext())
        {
            var user = User.Create(Email.Create("ponta-a-ponta@exemplo.com").Value, "Usuário Ponta a Ponta", "hash", timeProvider).Value;
            context.Users.Add(user);
            await context.SaveChangesAsync();
            userId = user.Id;
        }

        using var client = CreateClient();

        var antes = await client.ValidateUserAsync(new ValidateUserRequest { UserId = userId.ToString() });
        antes.Exists.Should().BeTrue();
        antes.Active.Should().BeTrue();
        antes.DisplayName.Should().Be("Usuário Ponta a Ponta");

        await using (var context = CreateContext())
        {
            var user = await context.Users.SingleAsync(u => u.Id == userId);
            user.Deactivate(timeProvider);
            await context.SaveChangesAsync();
        }

        var depois = await client.ValidateUserAsync(new ValidateUserRequest { UserId = userId.ToString() });
        depois.Exists.Should().BeTrue();
        depois.Active.Should().BeFalse("desativar no banco precisa refletir na próxima chamada gRPC, sem reiniciar o serviço (CA-13)");
    }

    [Fact]
    [Trait("Category", "Docker")]
    public async Task ValidateUser_ComProviderPersistido_UsuarioInexistente_RetornaExistsFalse()
    {
        using var client = CreateClient();

        var response = await client.ValidateUserAsync(new ValidateUserRequest { UserId = Guid.NewGuid().ToString() });

        response.Exists.Should().BeFalse();
        response.Active.Should().BeFalse();
        response.DisplayName.Should().Be(string.Empty);
    }

    private IdentityGrpcTestClient CreateClient()
    {
        var handler = _factory.Server.CreateHandler();
        return new IdentityGrpcTestClient(handler, _factory.Server.BaseAddress);
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
