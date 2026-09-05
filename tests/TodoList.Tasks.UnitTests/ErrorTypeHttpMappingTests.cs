using FluentAssertions;
using Microsoft.AspNetCore.Http;
using TodoList.SharedKernel;
using TodoList.Tasks.Api.ResultMapping;
using Xunit;

namespace TodoList.Tasks.UnitTests;

/// <summary>
/// Mapeamento ErrorType → status HTTP no Tasks Service (BE-03, CA-02) —
/// parametrizado sobre todos os valores do enum. A tabela é idêntica à do
/// Identity (CA-09): os dois serviços mapeiam o mesmo <see cref="ErrorType"/>
/// para o mesmo status HTTP, mesmo com a extensão duplicada por design (D-26).
/// </summary>
public class ErrorTypeHttpMappingTests
{
    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorType.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.TooManyRequests, StatusCodes.Status429TooManyRequests)]
    [InlineData(ErrorType.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(ErrorType.Failure, StatusCodes.Status500InternalServerError)]
    public void ToStatusCode_MapeiaCadaErrorTypeParaOHttpEsperado(ErrorType type, int statusCodeEsperado)
    {
        type.ToStatusCode().Should().Be(statusCodeEsperado);
    }

    [Fact]
    public void ToStatusCode_CobreTodosOsValoresDoEnum()
    {
        var todosOsValores = Enum.GetValues<ErrorType>();

        foreach (var valor in todosOsValores)
        {
            var act = () => valor.ToStatusCode();

            act.Should().NotThrow($"ErrorType.{valor} precisa de mapeamento HTTP (CA-02)");
        }
    }
}
