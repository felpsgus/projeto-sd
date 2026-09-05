using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TodoList.Identity.Infrastructure.Users;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// Seed em memória do Identity — BE-26 CA-14 (exatamente dois usuários fixos,
/// um ativo e um inativo) e CA-15 (aviso de inicialização).
/// </summary>
public class InMemoryUserLookupTests
{
    [Fact] // CA-14
    public async Task FindByIdAsync_UsuarioAtivoFixo_RetornaActiveTrue()
    {
        var sut = new InMemoryUserLookup(Substitute.For<ILogger<InMemoryUserLookup>>());

        var result = await sut.FindByIdAsync(InMemoryUserLookup.ActiveUserId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Active.Should().BeTrue();
        result.DisplayName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact] // CA-14
    public async Task FindByIdAsync_UsuarioInativoFixo_RetornaActiveFalse()
    {
        var sut = new InMemoryUserLookup(Substitute.For<ILogger<InMemoryUserLookup>>());

        var result = await sut.FindByIdAsync(InMemoryUserLookup.InactiveUserId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Active.Should().BeFalse();
        result.DisplayName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact] // CA-14
    public void ActiveUserId_And_InactiveUserId_SaoDiferentes()
    {
        InMemoryUserLookup.ActiveUserId.Should().NotBe(InMemoryUserLookup.InactiveUserId);
    }

    [Fact] // CA-15
    public void Constructor_EmitLogDeAvisoIndicandoQueOStorePersistidoNaoEstaEmUso()
    {
        var logger = Substitute.For<ILogger<InMemoryUserLookup>>();
        logger.IsEnabled(LogLevel.Warning).Returns(true);

        _ = new InMemoryUserLookup(logger);

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }
}
