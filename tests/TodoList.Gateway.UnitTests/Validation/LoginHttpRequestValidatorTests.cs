using FluentAssertions;
using TodoList.Gateway.Api.Contracts;
using TodoList.Gateway.Api.Validation;
using Xunit;

namespace TodoList.Gateway.UnitTests.Validation;

/// <summary>BE-36 — cobertura de unidade do validador de borda de <c>POST /api/auth/login</c>.</summary>
public class LoginHttpRequestValidatorTests
{
    private readonly LoginHttpRequestValidator _validator = new();

    [Theory]
    [InlineData(null, "senha123")]
    [InlineData("", "senha123")]
    [InlineData("user@example.com", null)]
    [InlineData("user@example.com", "")]
    public void Validate_EmailOuSenhaAusente_Invalido(string? email, string? password)
    {
        var result = _validator.Validate(new LoginHttpRequest(email, password));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmailESenhaPresentes_Valido()
    {
        var result = _validator.Validate(new LoginHttpRequest("user@example.com", "senha123"));

        result.IsValid.Should().BeTrue();
    }
}
