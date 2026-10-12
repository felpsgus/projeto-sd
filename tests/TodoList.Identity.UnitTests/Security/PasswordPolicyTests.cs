using FluentAssertions;
using TodoList.Identity.Application.Security;
using Xunit;

namespace TodoList.Identity.UnitTests.Security;

/// <summary>
/// <see cref="PasswordPolicy"/> (BE-06, RN-AUTH-04) — CA-06 a CA-08.
/// </summary>
public class PasswordPolicyTests
{
    [Theory] // CA-06
    [InlineData("abc12345")]
    [InlineData("Senha123")]
    public void Validate_ComSenhaValida_RetornaListaVazia(string senha)
    {
        var erros = PasswordPolicy.Validate(senha);

        erros.Should().BeEmpty();
    }

    [Fact] // CA-06 — 7 caracteres
    public void Validate_ComSenhaCurta_RetornaTooShort()
    {
        var erros = PasswordPolicy.Validate("abc1234");

        erros.Should().ContainSingle().Which.Should().Be(PasswordErrors.TooShort);
    }

    [Fact] // CA-06 — sem número
    public void Validate_SemNumero_RetornaMissingNumber()
    {
        var erros = PasswordPolicy.Validate("abcdefgh");

        erros.Should().ContainSingle().Which.Should().Be(PasswordErrors.MissingNumber);
    }

    [Fact] // CA-06 — sem letra
    public void Validate_SemLetra_RetornaMissingLetter()
    {
        var erros = PasswordPolicy.Validate("12345678");

        erros.Should().ContainSingle().Which.Should().Be(PasswordErrors.MissingLetter);
    }

    [Fact] // CA-06
    public void Validate_ComSenhaVazia_RetornaFalha()
    {
        var erros = PasswordPolicy.Validate(string.Empty);

        erros.Should().NotBeEmpty();
    }

    [Fact] // CA-06
    public void Validate_ComSenhaNula_RetornaFalha()
    {
        var erros = PasswordPolicy.Validate(null);

        erros.Should().NotBeEmpty();
    }

    [Fact] // CA-07 — duas regras violadas ao mesmo tempo (curta e sem número)
    public void Validate_ComSenhaQueViolaDuasRegras_RetornaDuasMensagens()
    {
        var erros = PasswordPolicy.Validate("abcdefg");

        erros.Should().HaveCount(2);
        erros.Should().Contain(PasswordErrors.TooShort);
        erros.Should().Contain(PasswordErrors.MissingNumber);
    }

    [Fact] // CA-08 — mensagens de erro não contêm a senha informada
    public void Validate_ComSenhaInvalida_MensagensDeErroNaoContemASenha()
    {
        const string senha = "segredoSuperSecreto1";
        var erros = PasswordPolicy.Validate(senha[..3]); // corta para violar o mínimo de caracteres

        erros.Should().NotBeEmpty();
        erros.Should().OnlyContain(erro => !erro.Message.Contains(senha, StringComparison.Ordinal));
    }
}
