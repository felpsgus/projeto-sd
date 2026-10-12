using FluentAssertions;
using TodoList.Gateway.Api.Contracts;
using TodoList.Gateway.Api.Validation;
using Xunit;

namespace TodoList.Gateway.UnitTests.Validation;

/// <summary>BE-14 — cobertura de unidade do validador de borda de <c>PATCH /api/me</c> (CA-06/CA-07).</summary>
public class UpdateProfileHttpRequestValidatorTests
{
    private readonly UpdateProfileHttpRequestValidator _validator = new();

    [Theory] // CA-06
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_DisplayNameAusenteOuSoEspacos_Invalido(string? displayName)
    {
        var result = _validator.Validate(new UpdateProfileHttpRequest(displayName));

        result.IsValid.Should().BeFalse();
    }

    [Fact] // CA-06: 101 caracteres é rejeitado
    public void Validate_DisplayNameAcimaDoLimite_Invalido()
    {
        var result = _validator.Validate(new UpdateProfileHttpRequest(new string('a', 101)));

        result.IsValid.Should().BeFalse();
    }

    [Fact] // CA-07: borda de 1 caractere é aceita
    public void Validate_DisplayNameComUmCaractere_Valido()
    {
        var result = _validator.Validate(new UpdateProfileHttpRequest("a"));

        result.IsValid.Should().BeTrue();
    }

    [Fact] // CA-07: borda de 100 caracteres é aceita
    public void Validate_DisplayNameComCemCaracteres_Valido()
    {
        var result = _validator.Validate(new UpdateProfileHttpRequest(new string('a', 100)));

        result.IsValid.Should().BeTrue();
    }

    [Fact] // BE-14, nota técnica: o trim conta para o limite, não o comprimento cru
    public void Validate_DisplayNameComEspacosNasBordasDentroDoLimiteAposTrim_Valido()
    {
        var result = _validator.Validate(new UpdateProfileHttpRequest("  " + new string('a', 100) + "  "));

        result.IsValid.Should().BeTrue();
    }
}
