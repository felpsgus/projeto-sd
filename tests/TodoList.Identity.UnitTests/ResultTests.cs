using FluentAssertions;
using TodoList.SharedKernel;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// Contrato de <see cref="Result"/>/<see cref="Result{TValue}"/> (BE-03, CA-01).
/// Testado a partir do Identity porque o tipo é comum aos dois serviços
/// (D-26) — não há teste duplicado equivalente no Tasks para esse contrato
/// em si, só para o mapeamento HTTP (que é duplicado de propósito).
/// </summary>
public class ResultTests
{
    [Fact]
    public void Value_EmResultadoDeFalha_Lanca()
    {
        var result = Result.Failure<int>(new Error("test.code", "mensagem", ErrorType.Validation));

        var act = () => result.Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Error_EmResultadoDeSucesso_Lanca()
    {
        var result = Result.Success();

        var act = () => result.Error;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ErrorGenerico_EmResultadoDeSucesso_Lanca()
    {
        var result = Result.Success(42);

        var act = () => result.Error;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Value_EmResultadoDeSucesso_RetornaValor()
    {
        var result = Result.Success(42);

        result.Value.Should().Be(42);
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
    }

    [Fact]
    public void Error_EmResultadoDeFalha_RetornaErro()
    {
        var error = new Error("test.code", "mensagem", ErrorType.Conflict);
        var result = Result.Failure(error);

        result.Error.Should().Be(error);
        result.IsFailure.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ConversaoImplicita_DeValorParaResultadoDeSucesso()
    {
        Result<int> result = 42;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }
}
