using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Users;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// <see cref="PersistedUserLookup"/> com <see cref="IUserRepository"/>
/// substituído — BE-26 CA-13 (parte que não precisa de banco real; a parte
/// "reflete sem reiniciar" é coberta pelo teste de integração com Postgres).
/// </summary>
public class PersistedUserLookupTests
{
    [Fact]
    public async Task FindByIdAsync_UsuarioExistente_RetornaActiveEDisplayNameDoRepositorio()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var user = User.Create(Email.Create("ativo@exemplo.com").Value, "Usuário Ativo", "hash", timeProvider).Value;

        var repository = Substitute.For<IUserRepository>();
        repository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var sut = new PersistedUserLookup(repository);

        var result = await sut.FindByIdAsync(user.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Active.Should().BeTrue();
        result.DisplayName.Should().Be("Usuário Ativo");
    }

    [Fact]
    public async Task FindByIdAsync_UsuarioInativo_RetornaActiveFalse()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var user = User.Create(Email.Create("inativo@exemplo.com").Value, "Usuário Inativo", "hash", timeProvider).Value;
        user.Deactivate(timeProvider);

        var repository = Substitute.For<IUserRepository>();
        repository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var sut = new PersistedUserLookup(repository);

        var result = await sut.FindByIdAsync(user.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Active.Should().BeFalse();
    }

    [Fact]
    public async Task FindByIdAsync_UsuarioInexistente_RetornaNull()
    {
        var repository = Substitute.For<IUserRepository>();
        repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var sut = new PersistedUserLookup(repository);

        var result = await sut.FindByIdAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }
}
