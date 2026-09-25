using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Persistence;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Users;
using Xunit;

namespace TodoList.Identity.UnitTests.Users;

/// <summary><see cref="UpdateProfileHandler"/> (BE-14) — CA-05, CA-06, CA-08, CA-09, CA-11.</summary>
public class UpdateProfileHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
    private readonly User _user;

    public UpdateProfileHandlerTests()
    {
        _user = User.Create(Email.Create("ada@exemplo.com").Value, "Ada", "hash-qualquer", _timeProvider).Value;
        _userRepository.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
    }

    [Fact] // CA-05/CA-08: nome válido é salvo com trim e persiste
    public async Task HandleAsync_NomeValidoComEspacos_SalvaComTrim()
    {
        var sut = new UpdateProfileHandler(_userRepository, _unitOfWork, _timeProvider);

        var result = await sut.HandleAsync(_user.Id, "  Novo Nome  ", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DisplayName.Should().Be("Novo Nome");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory] // CA-06
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_NomeVazioOuSoEspacos_RetornaErroDeValidacao(string displayName)
    {
        var sut = new UpdateProfileHandler(_userRepository, _unitOfWork, _timeProvider);

        var result = await sut.HandleAsync(_user.Id, displayName, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(UserErrors.DisplayNameEmpty);
    }

    [Fact] // CA-06: acima de 100 caracteres
    public async Task HandleAsync_Nome101Caracteres_RetornaErroDeValidacao()
    {
        var sut = new UpdateProfileHandler(_userRepository, _unitOfWork, _timeProvider);

        var result = await sut.HandleAsync(_user.Id, new string('a', 101), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(UserErrors.DisplayNameTooLong);
    }

    [Fact] // CA-07: bordas de 1 e 100 caracteres aceitas
    public async Task HandleAsync_Nome100Caracteres_EAceito()
    {
        var sut = new UpdateProfileHandler(_userRepository, _unitOfWork, _timeProvider);

        var result = await sut.HandleAsync(_user.Id, new string('a', 100), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact] // CA-09: e-mail nunca muda — a mensagem/handler nem recebe campo de e-mail
    public async Task HandleAsync_AlteraApenasONome_EmailPermaneceInalterado()
    {
        var sut = new UpdateProfileHandler(_userRepository, _unitOfWork, _timeProvider);
        var emailAntes = _user.Email.Value;

        await sut.HandleAsync(_user.Id, "Novo Nome", CancellationToken.None);

        _user.Email.Value.Should().Be(emailAntes);
    }

    [Fact] // CA-11: UpdatedAt muda, CreatedAt não
    public async Task HandleAsync_AoRenomear_AtualizaUpdatedAtMasNaoCreatedAt()
    {
        var sut = new UpdateProfileHandler(_userRepository, _unitOfWork, _timeProvider);
        var createdAtAntes = _user.CreatedAt;
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        await sut.HandleAsync(_user.Id, "Novo Nome", CancellationToken.None);

        _user.CreatedAt.Should().Be(createdAtAntes);
        _user.UpdatedAt.Should().BeAfter(createdAtAntes);
    }
}
