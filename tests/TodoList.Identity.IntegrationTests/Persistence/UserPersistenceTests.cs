using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Persistence;
using Xunit;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// Persistência real de <see cref="User"/> contra Postgres via Testcontainers
/// (BE-04) — CA-10 (e-mail duplicado viola o índice único do banco), CA-11
/// (duplicidade só de caixa também viola) e CA-12 (<see cref="User.PasswordHash"/>
/// nunca aparece numa serialização JSON de um usuário criado e consultado).
/// <b>Requer Docker.</b> A migration real do BE-04 (<c>AddUsersTable</c>) é
/// quem cria <c>identity.users</c> aqui — <c>Database.MigrateAsync()</c>, não
/// <c>EnsureCreated()</c>, para provar a migration de verdade, não só o
/// modelo atual do EF.
/// </summary>
[Collection("Postgres")]
public class UserPersistenceTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private FakeTimeProvider _timeProvider = null!;

    public UserPersistenceTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        await _fixture.EnsureStartedAsync();

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("Category", "Docker")]
    public async Task SaveChangesAsync_DoisUsuariosComMesmoEmail_ViolaIndiceUnicoDoBanco() // CA-10
    {
        var email = Email.Create("duplicado@exemplo.com").Value;

        await using (var context = CreateContext())
        {
            var primeiro = User.Create(email, "Primeiro Usuário", "hash-1", _timeProvider).Value;
            context.Users.Add(primeiro);
            await context.SaveChangesAsync();
        }

        await using var contextComDuplicata = CreateContext();
        var segundo = User.Create(email, "Segundo Usuário", "hash-2", _timeProvider).Value;
        contextComDuplicata.Users.Add(segundo);

        var act = async () => await contextComDuplicata.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "o índice único do banco precisa rejeitar e-mail duplicado (CA-10) — não é só a checagem em memória do caso de uso");
    }

    [Fact]
    [Trait("Category", "Docker")]
    public async Task SaveChangesAsync_EmailsDiferindoSoEmCaixa_TambemViolaIndiceUnico() // CA-11
    {
        await using (var context = CreateContext())
        {
            var primeiro = User.Create(Email.Create("Fulano@Exemplo.com").Value, "Fulano", "hash-1", _timeProvider).Value;
            context.Users.Add(primeiro);
            await context.SaveChangesAsync();
        }

        await using var contextComDuplicata = CreateContext();
        // Mesmo endereço, só variando caixa — Email.Create já normaliza para o
        // mesmo valor persistido ("fulano@exemplo.com"), então este segundo
        // insert tenta gravar exatamente a mesma string na coluna única.
        var segundo = User.Create(Email.Create("fulano@EXEMPLO.COM").Value, "Outro Fulano", "hash-2", _timeProvider).Value;
        contextComDuplicata.Users.Add(segundo);

        var act = async () => await contextComDuplicata.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "e-mails que diferem só em maiúsculas/minúsculas são o mesmo usuário (CA-11) — o banco precisa rejeitar igual");
    }

    [Fact]
    [Trait("Category", "Docker")]
    public async Task CriarEConsultarUsuario_SerializacaoJson_NuncaContemPasswordHash() // CA-12
    {
        const string senhaHash = "hash-super-secreto-nao-pode-vazar";
        Guid idSalvo;

        await using (var context = CreateContext())
        {
            var usuario = User.Create(Email.Create("serializacao@exemplo.com").Value, "Usuário Serializado", senhaHash, _timeProvider).Value;
            context.Users.Add(usuario);
            await context.SaveChangesAsync();
            idSalvo = usuario.Id;
        }

        // Novo contexto — o usuário consultado abaixo vem do banco, não do change tracker em memória.
        await using var novoContexto = CreateContext();
        var lido = await novoContexto.Users.SingleAsync(user => user.Id == idSalvo);

        var json = JsonSerializer.Serialize(lido);

        json.Should().NotContain(senhaHash, "PasswordHash não deve aparecer em nenhuma serialização JSON produzida pela aplicação (CA-12, RN-AUTH-05)");
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
