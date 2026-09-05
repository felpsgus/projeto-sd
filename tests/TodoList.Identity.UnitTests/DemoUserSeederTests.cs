using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TodoList.Identity.Application.Persistence;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Users;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// <see cref="DemoUserSeeder"/> (BE-04/BE-26 CA-14) com <see cref="IUserRepository"/>
/// substituído — cobre a idempotência (rodar quando os usuários já existem
/// não duplica) sem precisar de banco real.
/// </summary>
public class DemoUserSeederTests
{
    [Fact]
    public async Task SeedAsync_QuandoUsuariosNaoExistem_AdicionaOsDoisComOsIdsFixosDoInMemoryUserLookup()
    {
        var repository = Substitute.For<IUserRepository>();
        repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var sut = CreateSeeder(repository, unitOfWork);

        await sut.SeedAsync(CancellationToken.None);

        repository.Received(1).Add(Arg.Is<User>(user =>
            user.Id == InMemoryUserLookup.ActiveUserId && user.IsActive));
        repository.Received(1).Add(Arg.Is<User>(user =>
            user.Id == InMemoryUserLookup.InactiveUserId && !user.IsActive));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // idempotência: rodar de novo não duplica nem falha
    public async Task SeedAsync_QuandoUsuariosJaExistem_NaoAdicionaDeNovoENaoSalva()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var usuarioExistente = User.Create(
            InMemoryUserLookup.ActiveUserId, Email.Create("ja-existe@exemplo.com").Value, "Já Existe", "hash", timeProvider).Value;

        var repository = Substitute.For<IUserRepository>();
        repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(usuarioExistente);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var sut = CreateSeeder(repository, unitOfWork);

        await sut.SeedAsync(CancellationToken.None);

        repository.DidNotReceive().Add(Arg.Any<User>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // idempotência: rodar a seeder duas vezes seguidas não duplica nem falha
    public async Task SeedAsync_ChamadoDuasVezesSeguidas_NaSegundaVezNaoAdicionaNemFalha()
    {
        var estadoDoBanco = new Dictionary<Guid, User>();
        var repository = Substitute.For<IUserRepository>();
        repository
            .GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => estadoDoBanco.GetValueOrDefault(callInfo.ArgAt<Guid>(0)));
        repository
            .When(r => r.Add(Arg.Any<User>()))
            .Do(callInfo => estadoDoBanco[callInfo.Arg<User>().Id] = callInfo.Arg<User>());
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var sut = CreateSeeder(repository, unitOfWork);

        await sut.SeedAsync(CancellationToken.None);
        var act = async () => await sut.SeedAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        repository.Received(1).Add(Arg.Is<User>(user => user.Id == InMemoryUserLookup.ActiveUserId));
        repository.Received(1).Add(Arg.Is<User>(user => user.Id == InMemoryUserLookup.InactiveUserId));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static DemoUserSeeder CreateSeeder(IUserRepository repository, IUnitOfWork unitOfWork) =>
        new(repository, unitOfWork, new FakeTimeProvider(DateTimeOffset.UtcNow), NullLogger<DemoUserSeeder>.Instance);
}
