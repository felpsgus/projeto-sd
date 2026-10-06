using FluentAssertions;
using TodoList.Gateway.Api.Contracts;
using TodoList.Gateway.Api.Validation;
using Xunit;

namespace TodoList.Gateway.UnitTests.Validation;

/// <summary>BE-16 — cobertura de unidade do validador de borda de <c>DELETE /api/me</c>.</summary>
public class DeleteAccountHttpRequestValidatorTests
{
    private readonly DeleteAccountHttpRequestValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_SenhaAusente_Invalido(string? password)
    {
        var result = _validator.Validate(new DeleteAccountHttpRequest(password));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_SenhaPresente_Valido()
    {
        var result = _validator.Validate(new DeleteAccountHttpRequest("senha-correta-123"));

        result.IsValid.Should().BeTrue();
    }
}
