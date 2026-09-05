using FluentAssertions;
using TodoList.Identity.Domain.Users;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// Value object <see cref="Email"/> (BE-04) — CA-01 a CA-03.
/// </summary>
public class EmailTests
{
    [Theory] // CA-01
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sem-arroba.com")]
    [InlineData("usuario@semdominio")]
    [InlineData("us er@exemplo.com")]
    public void Create_ComEntradaInvalida_RetornaFalhaSemLancar(string? entrada)
    {
        var result = Email.Create(entrada);

        result.IsFailure.Should().BeTrue();
    }

    [Fact] // CA-01
    public void Create_ComMaisDe254Caracteres_RetornaFalha()
    {
        var localPartMuitoLongo = new string('a', 250);
        var emailMuitoLongo = $"{localPartMuitoLongo}@ex.com";
        emailMuitoLongo.Length.Should().BeGreaterThan(254, "o cenário só vale se realmente ultrapassar o limite");

        var result = Email.Create(emailMuitoLongo);

        result.IsFailure.Should().BeTrue();
    }

    [Fact] // CA-02
    public void Create_ComEspacosECaixaMista_NormalizaTrimELowercase()
    {
        var result = Email.Create(" João@Exemplo.COM ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be("joão@exemplo.com");
    }

    [Fact] // CA-03
    public void Create_MesmoEmailComCaixaDiferente_SaoIguaisPorEqualsEGetHashCode()
    {
        var email1 = Email.Create("A@X.com").Value;
        var email2 = Email.Create("a@x.com").Value;

        email1.Equals(email2).Should().BeTrue();
        email1.GetHashCode().Should().Be(email2.GetHashCode());
    }

    [Fact] // CA-03
    public void Create_EmailsDiferentes_NaoSaoIguais()
    {
        var email1 = Email.Create("a@x.com").Value;
        var email2 = Email.Create("b@x.com").Value;

        email1.Equals(email2).Should().BeFalse();
    }
}
