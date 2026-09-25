using FluentAssertions;
using TodoList.Gateway.Api.Contracts;
using TodoList.Gateway.Api.Validation;
using Xunit;

namespace TodoList.Gateway.UnitTests.Validation;

/// <summary>BE-15 — cobertura de unidade do validador de borda de <c>POST /api/me/change-password</c>.</summary>
public class ChangePasswordHttpRequestValidatorTests
{
    private readonly ChangePasswordHttpRequestValidator _validator = new();

    [Theory]
    [InlineData(null, "senha-nova-123")]
    [InlineData("", "senha-nova-123")]
    [InlineData("senha-atual-123", null)]
    [InlineData("senha-atual-123", "")]
    public void Validate_CamposAusentes_Invalido(string? currentPassword, string? newPassword)
    {
        var result = _validator.Validate(new ChangePasswordHttpRequest(currentPassword, newPassword));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_AmbosPresentes_Valido()
    {
        var result = _validator.Validate(new ChangePasswordHttpRequest("senha-atual-123", "senha-nova-123"));

        result.IsValid.Should().BeTrue();
    }
}
