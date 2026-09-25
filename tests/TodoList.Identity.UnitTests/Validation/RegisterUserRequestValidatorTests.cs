using FluentAssertions;
using TodoList.Identity.Api.Validation;
using TodoList.Identity.Application.Authentication;
using Xunit;

namespace TodoList.Identity.UnitTests.Validation;

/// <summary>
/// <see cref="RegisterUserRequestValidator"/> (BE-07) — CA-07 (formato de
/// e-mail) e CA-08 (política de senha, com todas as violações reportadas).
/// </summary>
public class RegisterUserRequestValidatorTests
{
    private readonly RegisterUserRequestValidator _sut = new();

    [Theory] // CA-07
    [InlineData("nao-e-email")]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_EmailInvalido_ApontaCampoEmail(string? email)
    {
        var result = _sut.Validate(new RegisterUserRequest(email, "senha123", null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact] // CA-08: violações múltiplas reportadas de uma vez
    public void Validate_SenhaComDuasViolacoes_RetornaDuasMensagens()
    {
        var result = _sut.Validate(new RegisterUserRequest("ada@exemplo.com", "abcdefg", null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2, "curta e sem número são duas violações da política (RN-AUTH-04)");
    }

    [Fact] // CA-01/senha válida
    public void Validate_DadosValidos_SemErros()
    {
        var result = _sut.Validate(new RegisterUserRequest("ada@exemplo.com", "senha123", "Ada"));

        result.IsValid.Should().BeTrue();
    }
}
