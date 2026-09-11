using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Identity.Infrastructure.Security;
using TodoList.Identity.Infrastructure.Users;
using Xunit;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// <see cref="DemoUserSeeder"/> contra Postgres real — confirma que os dois
/// usuários de demonstração são persistidos de verdade com os ids fixos do
/// <see cref="InMemoryUserLookup"/> (necessário para a FK cruzada
/// <c>tasks.tasks.owner_id → identity.users(id)</c>, nota do README), que
/// rodar a seeder duas vezes seguidas contra o mesmo banco não duplica nem
/// falha, e que a senha real (BE-33) é sincronizada de verdade contra o
/// índice único de e-mail. <b>Requer Docker.</b>
/// </summary>
[Collection("Postgres")]
public class DemoUserSeederIntegrationTests : IAsyncLifetime
{
    private const string DemoPassword = "senha-de-demonstracao-para-teste-123";
    private const string OutraDemoPassword = "outra-senha-de-demonstracao-456";

    private readonly Pbkdf2PasswordHasher _passwordHasher = new(Options.Create(new PasswordHashingOptions { Iterations = 200 }));
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
            await seeder.SeedAsync(DemoPassword, CancellationToken.None);
        }

        await using (var context = CreateContext())
        {
            var seeder = CreateSeeder(context);
            var act = async () => await seeder.SeedAsync(DemoPassword, CancellationToken.None);

            await act.Should().NotThrowAsync("rodar o seed duas vezes não pode duplicar nem falhar");
        }

        await using var verificacao = CreateContext();
        var total = await verificacao.Users.CountAsync();
        total.Should().Be(2);

        var ativo = await verificacao.Users.SingleAsync(u => u.Id == InMemoryUserLookup.ActiveUserId);
        ativo.IsActive.Should().BeTrue();
        _passwordHasher.Verify(DemoPassword, ativo.PasswordHash).Should().BeTrue();

        var inativo = await verificacao.Users.SingleAsync(u => u.Id == InMemoryUserLookup.InactiveUserId);
        inativo.IsActive.Should().BeFalse();
        _passwordHasher.Verify(DemoPassword, inativo.PasswordHash).Should().BeTrue();
    }

    [Fact] // BE-33 CA-09/CA-10 — banco com o hash placeholder legado do T1 é regravado com hash real
    [Trait("Category", "Docker")]
    public async Task SeedAsync_ComUsuariosPreExistentesComHashPlaceholderDoT1_RegravaParaHashReal()
    {
        const string placeholderLegado = "seed-placeholder-not-a-real-hash-be-06-pending";

        await using (var setup = CreateContext())
        {
            var emailAtivo = Email.Create("ada.lovelace@todolist.example").Value;
            var usuarioAtivo = User.Create(
                InMemoryUserLookup.ActiveUserId, emailAtivo, "Ada Lovelace", placeholderLegado, TimeProvider.System).Value;
            setup.Users.Add(usuarioAtivo);
            await setup.SaveChangesAsync();
        }

        await using (var antes = CreateContext())
        {
            var usuario = await antes.Users.SingleAsync(u => u.Id == InMemoryUserLookup.ActiveUserId);
            usuario.PasswordHash.Should().Be(placeholderLegado);
        }

        await using (var context = CreateContext())
        {
            var seeder = CreateSeeder(context);
            await seeder.SeedAsync(DemoPassword, CancellationToken.None);
        }

        await using var verificacao = CreateContext();
        var ativo = await verificacao.Users.SingleAsync(u => u.Id == InMemoryUserLookup.ActiveUserId);
        ativo.PasswordHash.Should().NotBe(placeholderLegado);
        _passwordHasher.Verify(DemoPassword, ativo.PasswordHash).Should().BeTrue();

        // Rodar de novo com a mesma senha não regrava (idempotência).
        var hashAposPrimeiraSincronizacao = ativo.PasswordHash;
        await using (var context = CreateContext())
        {
            var seeder = CreateSeeder(context);
            await seeder.SeedAsync(DemoPassword, CancellationToken.None);
        }

        await using var segundaVerificacao = CreateContext();
        var aindaAtivo = await segundaVerificacao.Users.SingleAsync(u => u.Id == InMemoryUserLookup.ActiveUserId);
        aindaAtivo.PasswordHash.Should().Be(hashAposPrimeiraSincronizacao);

        // Com outra senha, regrava de novo.
        await using (var context = CreateContext())
        {
            var seeder = CreateSeeder(context);
            await seeder.SeedAsync(OutraDemoPassword, CancellationToken.None);
        }

        await using var terceiraVerificacao = CreateContext();
        var comOutraSenha = await terceiraVerificacao.Users.SingleAsync(u => u.Id == InMemoryUserLookup.ActiveUserId);
        _passwordHasher.Verify(OutraDemoPassword, comOutraSenha.PasswordHash).Should().BeTrue();
    }

    private DemoUserSeeder CreateSeeder(IdentityDbContext context) =>
        new(new UserRepository(context), context, _passwordHasher, TimeProvider.System, NullLogger<DemoUserSeeder>.Instance);

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
