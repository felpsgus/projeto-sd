using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TodoList.Identity.Application.Persistence;
using TodoList.Identity.Application.Security;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Security;
using TodoList.Identity.Infrastructure.Users;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// <see cref="DemoUserSeeder"/> (BE-04/BE-26 CA-14, BE-33 CA-08/CA-09) com
/// <see cref="IUserRepository"/> substituído e um <see cref="IPasswordHasher"/>
/// real (custo reduzido) — cobre tanto a criação inicial quanto a
/// sincronização de senha, sem precisar de banco real.
/// </summary>
public class DemoUserSeederTests
{
    private const string DemoPassword = "senha-demo-123";
    private const string OutraSenha = "outra-senha-456";

    private readonly Pbkdf2PasswordHasher _passwordHasher = new(Options.Create(new PasswordHashingOptions { Iterations = 10 }));

    [Fact]
    public async Task SeedAsync_QuandoUsuariosNaoExistem_AdicionaOsDoisComOsIdsFixosDoInMemoryUserLookup()
    {
        var repository = Substitute.For<IUserRepository>();
        repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var sut = CreateSeeder(repository, unitOfWork);

        await sut.SeedAsync(DemoPassword, CancellationToken.None);

        repository.Received(1).Add(Arg.Is<User>(user =>
            user.Id == InMemoryUserLookup.ActiveUserId && user.IsActive && _passwordHasher.Verify(DemoPassword, user.PasswordHash)));
        repository.Received(1).Add(Arg.Is<User>(user =>
            user.Id == InMemoryUserLookup.InactiveUserId && !user.IsActive && _passwordHasher.Verify(DemoPassword, user.PasswordHash)));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // idempotência (CA-09 de BE-33): senha já confere, nada é regravado
    public async Task SeedAsync_QuandoUsuarioJaExisteComASenhaCorreta_NaoRegravaNemSalva()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var usuarioExistente = User.Create(
            InMemoryUserLookup.ActiveUserId, Email.Create("ja-existe@exemplo.com").Value, "Já Existe", _passwordHasher.Hash(DemoPassword), timeProvider).Value;

        var repository = Substitute.For<IUserRepository>();
        repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(usuarioExistente);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var sut = CreateSeeder(repository, unitOfWork, timeProvider);

        await sut.SeedAsync(DemoPassword, CancellationToken.None);

        repository.DidNotReceive().Add(Arg.Any<User>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // BE-33 CA-09: senha antiga (ex.: placeholder do T1) é regravada com a nova
    public async Task SeedAsync_QuandoUsuarioJaExisteComOutraSenha_RegravaOHashESalva()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var usuarioExistente = User.Create(
            InMemoryUserLookup.ActiveUserId, Email.Create("ja-existe@exemplo.com").Value, "Já Existe", _passwordHasher.Hash(OutraSenha), timeProvider).Value;

        var repository = Substitute.For<IUserRepository>();
        repository.GetByIdAsync(InMemoryUserLookup.ActiveUserId, Arg.Any<CancellationToken>()).Returns(usuarioExistente);
        repository.GetByIdAsync(InMemoryUserLookup.InactiveUserId, Arg.Any<CancellationToken>()).Returns((User?)null);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var sut = CreateSeeder(repository, unitOfWork, timeProvider);

        await sut.SeedAsync(DemoPassword, CancellationToken.None);

        _passwordHasher.Verify(DemoPassword, usuarioExistente.PasswordHash).Should().BeTrue();
        repository.DidNotReceive().Add(Arg.Is<User>(user => user.Id == InMemoryUserLookup.ActiveUserId));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // um hash malformado (ex.: o antigo placeholder do T1) nunca passa em Verify — é regravado como qualquer outra senha desatualizada
    public async Task SeedAsync_QuandoUsuarioJaExisteComHashMalformado_RegravaOHash()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var usuarioComHashInvalido = User.Create(
            InMemoryUserLookup.ActiveUserId, Email.Create("ja-existe@exemplo.com").Value, "Já Existe",
            "seed-placeholder-not-a-real-hash-be-06-pending", timeProvider).Value;

        var repository = Substitute.For<IUserRepository>();
        repository.GetByIdAsync(InMemoryUserLookup.ActiveUserId, Arg.Any<CancellationToken>()).Returns(usuarioComHashInvalido);
        repository.GetByIdAsync(InMemoryUserLookup.InactiveUserId, Arg.Any<CancellationToken>()).Returns((User?)null);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var sut = CreateSeeder(repository, unitOfWork, timeProvider);

        await sut.SeedAsync(DemoPassword, CancellationToken.None);

        _passwordHasher.Verify(DemoPassword, usuarioComHashInvalido.PasswordHash).Should().BeTrue();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // idempotência: rodar a seeder duas vezes seguidas com a mesma senha não duplica nem regrava na segunda
    public async Task SeedAsync_ChamadoDuasVezesSeguidasComAMesmaSenha_NaSegundaVezNaoAdicionaNemFalha()
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

        await sut.SeedAsync(DemoPassword, CancellationToken.None);
        var act = async () => await sut.SeedAsync(DemoPassword, CancellationToken.None);

        await act.Should().NotThrowAsync();
        repository.Received(1).Add(Arg.Is<User>(user => user.Id == InMemoryUserLookup.ActiveUserId));
        repository.Received(1).Add(Arg.Is<User>(user => user.Id == InMemoryUserLookup.InactiveUserId));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private DemoUserSeeder CreateSeeder(IUserRepository repository, IUnitOfWork unitOfWork, TimeProvider? timeProvider = null) =>
        new(repository, unitOfWork, _passwordHasher, timeProvider ?? new FakeTimeProvider(DateTimeOffset.UtcNow), NullLogger<DemoUserSeeder>.Instance);
}
