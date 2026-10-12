using FluentAssertions;
using TodoList.Gateway.Api.Contracts;
using TodoList.Gateway.Api.Validation;
using Xunit;

namespace TodoList.Gateway.UnitTests.Validation;

/// <summary>BE-07 — cobertura de unidade do validador de borda de <c>POST /api/auth/register</c> (CA-07/CA-08).</summary>
public class RegisterHttpRequestValidatorTests
{
    private readonly RegisterHttpRequestValidator _validator = new();

    [Fact]
    public void Validate_DadosValidos_Valido()
    {
        var result = _validator.Validate(new RegisterHttpRequest("user@example.com", "senha123", "Nome"));

        result.IsValid.Should().BeTrue();
    }

    [Fact] // CA-09/CA-10: displayName ausente ou só-espaços é aceito na borda — o Identity decide o tratamento
    public void Validate_DisplayNameAusenteOuSoEspacos_Valido()
    {
        _validator.Validate(new RegisterHttpRequest("user@example.com", "senha123", null)).IsValid.Should().BeTrue();
        _validator.Validate(new RegisterHttpRequest("user@example.com", "senha123", "   ")).IsValid.Should().BeTrue();
    }

    [Theory] // CA-07
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-email")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    public void Validate_EmailInvalido_Invalido(string? email)
    {
        var result = _validator.Validate(new RegisterHttpRequest(email, "senha123", null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterHttpRequest.Email));
    }

    [Theory] // CA-08: senha fora da política (RN-AUTH-04) — reporta erro no campo password
    [InlineData(null)]
    [InlineData("")]
    [InlineData("curta1")] // < 8 caracteres
    [InlineData("12345678")] // sem letra
    [InlineData("abcdefgh")] // sem número
    public void Validate_SenhaForaDaPolitica_Invalido(string? password)
    {
        var result = _validator.Validate(new RegisterHttpRequest("user@example.com", password, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterHttpRequest.Password));
    }

    [Fact] // CA-08: todas as violações são reportadas de uma vez, não só a primeira
    public void Validate_SenhaComMultiplasViolacoes_ReportaTodas()
    {
        var result = _validator.Validate(new RegisterHttpRequest("user@example.com", "abc", null));

        result.Errors.Should().HaveCountGreaterThanOrEqualTo(2);
    }
}
