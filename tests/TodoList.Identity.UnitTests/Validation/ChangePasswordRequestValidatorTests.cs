using FluentAssertions;
using TodoList.Identity.Api.Validation;
using TodoList.Identity.Application.Authentication;
using Xunit;

namespace TodoList.Identity.UnitTests.Validation;

/// <summary>
/// <see cref="ChangePasswordRequestValidator"/> (BE-15, CA-05) — a nova senha
/// fora da política reporta todas as violações de uma vez.
/// </summary>
public class ChangePasswordRequestValidatorTests
{
    private readonly ChangePasswordRequestValidator _sut = new();

    [Fact] // CA-05
    public void Validate_NovaSenhaComTresViolacoes_RetornaTresMensagens()
    {
        var result = _sut.Validate(new ChangePasswordRequest(Guid.NewGuid(), "senha-atual-123", ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(3, "vazia viola tamanho, letra e número (RN-AUTH-04)");
    }

    [Fact]
    public void Validate_NovaSenhaValida_SemErros()
    {
        var result = _sut.Validate(new ChangePasswordRequest(Guid.NewGuid(), "senha-atual-123", "novaSenha123"));

        result.IsValid.Should().BeTrue();
    }
}
