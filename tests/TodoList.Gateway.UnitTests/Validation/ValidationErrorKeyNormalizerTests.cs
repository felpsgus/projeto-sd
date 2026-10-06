using FluentAssertions;
using TodoList.Gateway.Api.Validation;
using Xunit;

namespace TodoList.Gateway.UnitTests.Validation;

/// <summary>
/// BE-41 — correção adjacente: as chaves de <c>errors</c> devem sair em
/// camelCase nas duas origens (ValidationFilter/FluentValidation local e o
/// trailer <c>validation-errors</c> do Tasks), nunca em PascalCase.
/// </summary>
public class ValidationErrorKeyNormalizerTests
{
    [Fact]
    public void ToCamelCaseKeys_ChavesEmPascalCase_ViramCamelCase()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["Title"] = ["obrigatório"],
            ["DueDate"] = ["formato inválido"],
        };

        var normalized = ValidationErrorKeyNormalizer.ToCamelCaseKeys(errors);

        normalized.Should().ContainKey("title");
        normalized.Should().ContainKey("dueDate");
        normalized.Should().NotContainKey("Title");
        normalized.Should().NotContainKey("DueDate");
        normalized["title"].Should().BeEquivalentTo(["obrigatório"]);
    }

    [Fact]
    public void ToCamelCaseKeys_ChaveJaEmCamelCase_PermaneceInalterada()
    {
        var errors = new Dictionary<string, string[]> { ["id"] = ["deve ser um Guid válido"] };

        var normalized = ValidationErrorKeyNormalizer.ToCamelCaseKeys(errors);

        normalized.Should().ContainKey("id");
    }

    [Fact]
    public void ToCamelCaseKeys_DicionarioVazio_DevolveVazio()
    {
        var normalized = ValidationErrorKeyNormalizer.ToCamelCaseKeys(new Dictionary<string, string[]>());

        normalized.Should().BeEmpty();
    }

    [Fact]
    public void ToCamelCaseKeys_ErrorsNulo_Lanca()
    {
        var act = () => ValidationErrorKeyNormalizer.ToCamelCaseKeys(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
