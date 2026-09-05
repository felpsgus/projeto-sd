using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Identity.Infrastructure.Users;
using Xunit;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// <see cref="UserRepository"/> contra Postgres real (BE-04) — em especial
/// <see cref="UserRepository.GetByEmailAsync"/>/<see cref="UserRepository.EmailExistsAsync"/>,
/// que comparam o value object <see cref="Email"/> inteiro (mapeado via
/// <c>HasConversion</c>) numa cláusula <c>Where</c> — precisa rodar contra o
/// provider real para confirmar que o EF traduz a comparação corretamente (não
/// é garantido só por compilar). <b>Requer Docker.</b>
/// </summary>
[Collection("Postgres")]
public class UserRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;

    public UserRepositoryIntegrationTests(PostgresContainerFixture fixture)
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
    public async Task GetByEmailAsync_ComEmailExistente_RetornaOUsuario()
    {
        var email = Email.Create("repositorio@exemplo.com").Value;

        await using (var context = CreateContext())
        {
            var user = User.Create(email, "Usuário Repositório", "hash", TimeProvider.System).Value;
            new UserRepository(context).Add(user);
            await context.SaveChangesAsync();
        }

        await using var novoContexto = CreateContext();
        var encontrado = await new UserRepository(novoContexto).GetByEmailAsync(email, CancellationToken.None);

        encontrado.Should().NotBeNull();
        encontrado!.Email.Should().Be(email);
        encontrado.DisplayName.Should().Be("Usuário Repositório");
    }

    [Fact]
    [Trait("Category", "Docker")]
    public async Task GetByEmailAsync_ComEmailInexistente_RetornaNull()
    {
        await using var context = CreateContext();

        var resultado = await new UserRepository(context).GetByEmailAsync(
            Email.Create("nao-existe@exemplo.com").Value, CancellationToken.None);

        resultado.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Docker")]
    public async Task EmailExistsAsync_ComEmailJaCadastrado_RetornaTrue_MesmoConsultandoComCaixaDiferente()
    {
        await using (var context = CreateContext())
        {
            var user = User.Create(Email.Create("Existe@Exemplo.com").Value, "Alguém", "hash", TimeProvider.System).Value;
            new UserRepository(context).Add(user);
            await context.SaveChangesAsync();
        }

        await using var novoContexto = CreateContext();
        var repository = new UserRepository(novoContexto);

        // Email.Create já normaliza para minúsculas antes da consulta — a
        // checagem em memória usa a mesma normalização que a escrita.
        var existe = await repository.EmailExistsAsync(Email.Create("EXISTE@exemplo.COM").Value, CancellationToken.None);

        existe.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Docker")]
    public async Task EmailExistsAsync_ComEmailNaoCadastrado_RetornaFalse()
    {
        await using var context = CreateContext();

        var existe = await new UserRepository(context).EmailExistsAsync(
            Email.Create("ninguem-tem-este@exemplo.com").Value, CancellationToken.None);

        existe.Should().BeFalse();
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
