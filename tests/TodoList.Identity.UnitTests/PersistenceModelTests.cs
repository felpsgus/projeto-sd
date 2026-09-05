using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TodoList.Identity.Infrastructure.Persistence;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// CA-13 de BE-02: inspeciona o <c>Model</c> do EF do <see cref="IdentityDbContext"/>
/// — não é revisão manual. Construir o contexto aqui não conecta a nenhum
/// banco: montar o <c>Model</c> é só reflexão sobre o próprio
/// <c>OnModelCreating</c>, então a connection string abaixo nunca chega a ser
/// usada de verdade (nem precisa ser válida).
/// </summary>
public class PersistenceModelTests
{
    [Fact] // CA-13
    public void IdentityDbContext_TemSchemaPadraoIdentity()
    {
        using var context = CreateContext();

        context.Model.GetDefaultSchema().Should().Be("identity");
    }

    [Fact] // CA-13
    public void IdentityDbContext_NaoMapeiaNenhumaEntidadeNoSchemaTasks()
    {
        using var context = CreateContext();

        context.Model.GetEntityTypes()
            .Should()
            .NotContain(
                entityType => entityType.GetSchema() == "tasks",
                "o IdentityDbContext não pode conhecer nada do schema do Tasks (CA-13, D-27)");
    }

    private static IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused;Password=unused")
            .Options;

        return new IdentityDbContext(options);
    }
}
