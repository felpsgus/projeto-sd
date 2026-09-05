using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TodoList.Contracts.Identity.V1;
using TodoList.Identity.Api.Grpc;
using TodoList.Identity.Application.Users;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// <see cref="IdentityGrpcService"/> com <see cref="IUserLookup"/> substituído —
/// BE-26, CA-05 a CA-09, CA-11.
/// </summary>
public class IdentityGrpcServiceTests
{
    private static readonly Guid _activeUserId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid _inactiveUserId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid _unknownUserId = Guid.Parse("30000000-0000-0000-0000-000000000003");

    private readonly IUserLookup _userLookup = Substitute.For<IUserLookup>();
    private readonly IdentityGrpcService _sut;

    public IdentityGrpcServiceTests()
    {
        _userLookup
            .FindByIdAsync(_activeUserId, Arg.Any<CancellationToken>())
            .Returns(new UserLookupResult(Active: true, DisplayName: "Ada Lovelace"));

        _userLookup
            .FindByIdAsync(_inactiveUserId, Arg.Any<CancellationToken>())
            .Returns(new UserLookupResult(Active: false, DisplayName: "Charles Babbage"));

        _userLookup
            .FindByIdAsync(_unknownUserId, Arg.Any<CancellationToken>())
            .Returns((UserLookupResult?)null);

        _sut = new IdentityGrpcService(_userLookup, NullLogger<IdentityGrpcService>.Instance);
    }

    [Fact] // CA-05
    public async Task ValidateUser_UsuarioExistenteEAtivo_RetornaExistsEActiveTrue()
    {
        var request = new ValidateUserRequest { UserId = _activeUserId.ToString() };

        var response = await _sut.ValidateUser(request, new FakeServerCallContext());

        response.Exists.Should().BeTrue();
        response.Active.Should().BeTrue();
        response.DisplayName.Should().Be("Ada Lovelace");
    }

    [Fact] // CA-06
    public async Task ValidateUser_UsuarioExistenteEInativo_RetornaActiveFalse()
    {
        var request = new ValidateUserRequest { UserId = _inactiveUserId.ToString() };

        var response = await _sut.ValidateUser(request, new FakeServerCallContext());

        response.Exists.Should().BeTrue();
        response.Active.Should().BeFalse();
        response.DisplayName.Should().Be("Charles Babbage");
    }

    [Fact] // CA-07
    public async Task ValidateUser_UsuarioInexistente_RetornaExistsFalseEDisplayNameVazio()
    {
        var request = new ValidateUserRequest { UserId = _unknownUserId.ToString() };

        var response = await _sut.ValidateUser(request, new FakeServerCallContext());

        response.Exists.Should().BeFalse();
        response.Active.Should().BeFalse();
        response.DisplayName.Should().Be(string.Empty);
    }

    [Theory] // CA-08
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public async Task ValidateUser_UserIdMalformado_RetornaRespostaNegativaSemLancar(string malformedUserId)
    {
        var request = new ValidateUserRequest { UserId = malformedUserId };

        var act = async () => await _sut.ValidateUser(request, new FakeServerCallContext());

        var response = await act.Should().NotThrowAsync();
        response.Subject.Exists.Should().BeFalse();
        response.Subject.Active.Should().BeFalse();
        response.Subject.DisplayName.Should().Be(string.Empty);
    }

    [Fact] // CA-09
    public async Task ValidateUser_Resposta_NaoContemCampoAlemDosTresDoContrato()
    {
        var request = new ValidateUserRequest { UserId = _activeUserId.ToString() };

        var response = await _sut.ValidateUser(request, new FakeServerCallContext());

        var descriptorFields = ValidateUserResponse.Descriptor.Fields.InFieldNumberOrder();
        descriptorFields.Select(field => field.Name).Should().BeEquivalentTo("exists", "active", "display_name");
    }

    [Fact] // CA-11
    public async Task ValidateToken_QualquerEntrada_RetornaValidFalseEUserIdVazio()
    {
        var request = new ValidateTokenRequest { AccessToken = "um-token-valido-de-verdade" };

        var response = await _sut.ValidateToken(request, new FakeServerCallContext());

        response.Valid.Should().BeFalse();
        response.UserId.Should().Be(string.Empty);
    }
}
