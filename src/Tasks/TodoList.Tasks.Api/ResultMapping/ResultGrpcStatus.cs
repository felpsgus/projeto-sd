using System.Text.Json;
using FluentValidation.Results;
using Grpc.Core;
using TodoList.SharedKernel;

namespace TodoList.Tasks.Api.ResultMapping;

/// <summary>
/// Espelho gRPC de <see cref="ResultHttpResults"/> (BE-03, BE-35) — converte
/// <see cref="Result"/>/<see cref="Result{TValue}"/> em <see cref="RpcException"/>,
/// com o <see cref="ErrorType"/> mapeado para <see cref="StatusCode"/> (D-35) e
/// o <see cref="Error.Code"/> do catálogo viajando no trailer
/// <c>error-code</c> — o <c>TaskReply</c> não tem (e não deve ganhar) campo de
/// erro (nota técnica de BE-35): quem sinaliza falha é sempre o
/// <see cref="StatusCode"/> gRPC, nunca o corpo da mensagem.
/// </summary>
public static class ResultGrpcStatus
{
    /// <summary>Trailer com o código estável do catálogo de erros (D-35), em todo caminho de falha.</summary>
    public const string ErrorCodeTrailerKey = "error-code";

    /// <summary>
    /// Trailer com o JSON <c>{ "campo": ["mensagem", ...] }</c> de uma falha de
    /// validação (BE-35, CA-03) — só presente quando <see cref="ErrorCodeTrailerKey"/>
    /// é <see cref="ValidationFailedErrorCode"/>.
    /// </summary>
    public const string ValidationErrorsTrailerKey = "validation-errors";

    /// <summary>Código de erro do trailer <c>error-code</c> para falha de validação (BE-35, CA-03).</summary>
    public const string ValidationFailedErrorCode = "validation.failed";

    /// <summary>Converte um <see cref="Result"/> sem valor em <see cref="RpcException"/> quando ele falhou.</summary>
    public static RpcException ToRpcException(this Result result)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("ToRpcException só pode ser chamado sobre um Result de falha.");
        }

        return result.Error.ToRpcException();
    }

    /// <summary>Converte um <see cref="Result{TValue}"/> em <see cref="RpcException"/> quando ele falhou.</summary>
    public static RpcException ToRpcException<TValue>(this Result<TValue> result)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("ToRpcException só pode ser chamado sobre um Result de falha.");
        }

        return result.Error.ToRpcException();
    }

    /// <summary>Converte um <see cref="Error"/> do catálogo de negócio em <see cref="RpcException"/> (D-35).</summary>
    public static RpcException ToRpcException(this Error error)
    {
        var statusCode = error.Type.ToGrpcStatusCode();
        var trailers = new Metadata { { ErrorCodeTrailerKey, error.Code } };

        return new RpcException(new Status(statusCode, error.Message), trailers);
    }

    /// <summary>
    /// Converte um <see cref="ValidationResult"/> do FluentValidation com falha
    /// em <see cref="RpcException"/> (BE-35, CA-03) — <see cref="StatusCode.InvalidArgument"/>,
    /// <c>error-code: validation.failed</c> e o dicionário por campo
    /// (<see cref="ValidationResult.ToDictionary"/>) serializado no trailer
    /// <see cref="ValidationErrorsTrailerKey"/>, o mesmo dicionário que
    /// <c>Api/Validation/ValidationFilter.cs</c> devolvia no corpo do
    /// <c>ValidationProblem</c> REST.
    /// </summary>
    public static RpcException ToValidationFailedException(this ValidationResult validationResult) =>
        ToValidationFailedException((IReadOnlyDictionary<string, string[]>)validationResult.ToDictionary());

    /// <summary>
    /// Mesma conversão de <see cref="ToValidationFailedException(ValidationResult)"/>,
    /// para um dicionário de erros já pronto (BE-35, CA-03 — o campo <c>due_date</c>
    /// fora do formato, detectado em <c>TaskGrpcMapping</c> antes do
    /// FluentValidation rodar).
    /// </summary>
    public static RpcException ToValidationFailedException(IReadOnlyDictionary<string, string[]> errors)
    {
        var trailers = new Metadata
        {
            { ErrorCodeTrailerKey, ValidationFailedErrorCode },
            { ValidationErrorsTrailerKey, JsonSerializer.Serialize(errors) },
        };

        return new RpcException(new Status(StatusCode.InvalidArgument, "Requisição inválida."), trailers);
    }
}

/// <summary>
/// Mapeamento <see cref="ErrorType"/> → <see cref="StatusCode"/> gRPC (D-35) —
/// mesma tabela de <see cref="ErrorTypeHttpMapping"/>, agora para o transporte
/// gRPC do Tasks Service (BE-35).
/// </summary>
public static class ErrorTypeGrpcMapping
{
    public static StatusCode ToGrpcStatusCode(this ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCode.InvalidArgument,
        ErrorType.Unauthorized => StatusCode.Unauthenticated,
        ErrorType.Forbidden => StatusCode.PermissionDenied,
        ErrorType.NotFound => StatusCode.NotFound,
        ErrorType.Conflict => StatusCode.FailedPrecondition,
        ErrorType.TooManyRequests => StatusCode.ResourceExhausted,
        ErrorType.Unavailable => StatusCode.Unavailable,
        ErrorType.Failure => StatusCode.Internal,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "ErrorType sem mapeamento gRPC definido."),
    };
}
