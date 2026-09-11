using System.Text.Json;
using FluentAssertions;
using FluentValidation.Results;
using Grpc.Core;
using TodoList.SharedKernel;
using TodoList.Tasks.Api.ResultMapping;
using Xunit;

namespace TodoList.Tasks.UnitTests.ResultMapping;

/// <summary>
/// <see cref="ErrorTypeGrpcMapping"/>/<see cref="ResultGrpcStatus"/> (BE-35,
/// D-35) — mesma cobertura de <c>ErrorTypeHttpMappingTests</c>, agora para o
/// transporte gRPC: todo <see cref="ErrorType"/> precisa de
/// <see cref="StatusCode"/> mapeado, o <c>error-code</c> sempre viaja no
/// trailer, e uma falha de validação carrega o dicionário por campo no
/// trailer <c>validation-errors</c>.
/// </summary>
public class ResultGrpcStatusTests
{
    [Theory]
    [InlineData(ErrorType.Validation, StatusCode.InvalidArgument)]
    [InlineData(ErrorType.Unauthorized, StatusCode.Unauthenticated)]
    [InlineData(ErrorType.Forbidden, StatusCode.PermissionDenied)]
    [InlineData(ErrorType.NotFound, StatusCode.NotFound)]
    [InlineData(ErrorType.Conflict, StatusCode.FailedPrecondition)]
    [InlineData(ErrorType.TooManyRequests, StatusCode.ResourceExhausted)]
    [InlineData(ErrorType.Unavailable, StatusCode.Unavailable)]
    [InlineData(ErrorType.Failure, StatusCode.Internal)]
    public void ToStatusCode_MapeiaCadaErrorTypeParaOStatusCodeGrpcEsperado(ErrorType type, StatusCode statusCodeEsperado)
    {
        type.ToGrpcStatusCode().Should().Be(statusCodeEsperado);
    }

    [Fact]
    public void ToStatusCode_CobreTodosOsValoresDoEnum()
    {
        foreach (var valor in Enum.GetValues<ErrorType>())
        {
            var act = () => valor.ToGrpcStatusCode();

            act.Should().NotThrow($"ErrorType.{valor} precisa de mapeamento gRPC (D-35)");
        }
    }

    [Fact]
    public void ToRpcException_ComErrorDeNegocio_CarregaOErrorCodeNoTrailer()
    {
        var error = new Error("task.owner_inactive", "O usuário informado está inativo.", ErrorType.Conflict);
        var result = Result.Failure<string>(error);

        var exception = result.ToRpcException();

        exception.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Status.Detail.Should().Be(error.Message);
        exception.Trailers.GetValue(ResultGrpcStatus.ErrorCodeTrailerKey).Should().Be(error.Code);
    }

    [Fact]
    public void ToRpcException_ComResultSemValor_CarregaOErrorCodeNoTrailer()
    {
        var error = new Error("identity.unavailable", "Indisponível.", ErrorType.Unavailable);
        var result = Result.Failure(error);

        var exception = result.ToRpcException();

        exception.StatusCode.Should().Be(StatusCode.Unavailable);
        exception.Trailers.GetValue(ResultGrpcStatus.ErrorCodeTrailerKey).Should().Be(error.Code);
    }

    [Fact]
    public void ToValidationFailedException_ComValidationResult_CarregaErrorCodeEDicionarioDeErros()
    {
        var validationResult = new ValidationResult(
        [
            new ValidationFailure("Title", "O título é obrigatório."),
        ]);

        var exception = validationResult.ToValidationFailedException();

        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
        exception.Trailers.GetValue(ResultGrpcStatus.ErrorCodeTrailerKey).Should().Be(ResultGrpcStatus.ValidationFailedErrorCode);

        var json = exception.Trailers.GetValue(ResultGrpcStatus.ValidationErrorsTrailerKey);
        json.Should().NotBeNullOrWhiteSpace();

        var errors = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json!);
        errors.Should().ContainKey("Title");
        errors!["Title"].Should().Contain("O título é obrigatório.");
    }

    [Fact]
    public void ToValidationFailedException_ComDicionarioPronto_CarregaErrorCodeEDicionarioDeErros()
    {
        var errors = new Dictionary<string, string[]> { ["DueDate"] = ["A data de vencimento deve estar no formato 'yyyy-MM-dd'."] };

        var exception = ResultGrpcStatus.ToValidationFailedException(errors);

        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
        exception.Trailers.GetValue(ResultGrpcStatus.ErrorCodeTrailerKey).Should().Be(ResultGrpcStatus.ValidationFailedErrorCode);

        var json = exception.Trailers.GetValue(ResultGrpcStatus.ValidationErrorsTrailerKey);
        var deserialized = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json!);
        deserialized.Should().ContainKey("DueDate");
    }

    [Fact]
    public void ToRpcException_ComResultDeSucesso_Lanca()
    {
        var result = Result.Success();

        var act = () => result.ToRpcException();

        act.Should().Throw<InvalidOperationException>();
    }
}
