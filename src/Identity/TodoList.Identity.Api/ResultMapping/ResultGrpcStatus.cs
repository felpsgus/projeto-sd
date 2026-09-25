using System.Text.Json;
using FluentValidation.Results;
using Grpc.Core;
using TodoList.SharedKernel;

namespace TodoList.Identity.Api.ResultMapping;

/// <summary>
/// Espelho gRPC de <see cref="ResultHttpResults"/> (BE-03), no mesmo desenho
/// de <c>TodoList.Tasks.Api.ResultMapping.ResultGrpcStatus</c> (BE-35, D-35) —
/// converte <see cref="Result"/>/<see cref="Result{TValue}"/> em
/// <see cref="RpcException"/>, com o <see cref="ErrorType"/> mapeado para
/// <see cref="StatusCode"/> e o <see cref="Error.Code"/> do catálogo viajando
/// no trailer <c>error-code</c>. Duplicado, não compartilhado com o Tasks
/// Service — mesma decisão de <c>ResultHttpResults</c> (D-26): poucas linhas,
/// e compartilhar acoplaria os dois serviços por um projeto extra.
///
/// <para>
/// Usado pelos RPCs novos da Fase 3 (BE-07/BE-14/BE-15/BE-16: <c>Register</c>,
/// <c>GetProfile</c>, <c>UpdateProfile</c>, <c>ChangePassword</c>,
/// <c>DeleteAccount</c>) — <c>Login</c>/<c>ValidateUser</c>/<c>ValidateToken</c>
/// continuam com o desenho "nunca falha, sempre <c>succeeded</c>/<c>valid</c>/<c>exists</c>"
/// de BE-26/BE-33, que não muda.
/// </para>
/// </summary>
public static class ResultGrpcStatus
{
    /// <summary>Trailer com o código estável do catálogo de erros, em todo caminho de falha.</summary>
    public const string ErrorCodeTrailerKey = "error-code";

    /// <summary>
    /// Trailer com o JSON <c>{ "campo": ["mensagem", ...] }</c> de uma falha de
    /// validação — só presente quando <see cref="ErrorCodeTrailerKey"/> é
    /// <see cref="ValidationFailedErrorCode"/>.
    /// </summary>
    public const string ValidationErrorsTrailerKey = "validation-errors";

    /// <summary>Código de erro do trailer <c>error-code</c> para falha de validação.</summary>
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

    /// <summary>Converte um <see cref="Error"/> do catálogo de negócio em <see cref="RpcException"/>.</summary>
    public static RpcException ToRpcException(this Error error)
    {
        var statusCode = error.Type.ToGrpcStatusCode();
        var trailers = new Metadata { { ErrorCodeTrailerKey, error.Code } };

        return new RpcException(new Status(statusCode, error.Message), trailers);
    }

    /// <summary>
    /// Mesma conversão de <see cref="ToRpcException(Error)"/>, mas anexando
    /// também o trailer <see cref="ValidationErrorsTrailerKey"/> com
    /// <paramref name="fieldErrors"/> — para um erro de negócio (não uma
    /// falha de <c>FluentValidation</c>) que ainda assim precisa apontar um
    /// campo do formulário no frontend.
    ///
    /// <para>
    /// Usado por <c>IdentityGrpcService.ChangePassword</c>/<c>DeleteAccount</c>
    /// quando <see cref="Authentication.AuthErrors.InvalidCurrentPassword"/>
    /// falha (decisão do tech lead, ver comentário no catálogo): o
    /// <c>error-code</c> continua <c>auth.invalid_current_password</c> — <b>
    /// nunca</b> <see cref="ValidationFailedErrorCode"/> — porque a causa não
    /// é uma falha de validação de borda, é uma regra de negócio; o trailer
    /// de campo é só para o frontend poder posicionar a mensagem, sem mudar o
    /// contrato de erro que o cliente já reconhece.
    /// </para>
    /// </summary>
    public static RpcException ToRpcException(this Error error, IReadOnlyDictionary<string, string[]> fieldErrors)
    {
        var statusCode = error.Type.ToGrpcStatusCode();
        var trailers = new Metadata
        {
            { ErrorCodeTrailerKey, error.Code },
            { ValidationErrorsTrailerKey, JsonSerializer.Serialize(fieldErrors) },
        };

        return new RpcException(new Status(statusCode, error.Message), trailers);
    }

    /// <summary>
    /// Converte um <see cref="ValidationResult"/> do FluentValidation com falha
    /// em <see cref="RpcException"/> — <see cref="StatusCode.InvalidArgument"/>,
    /// <c>error-code: validation.failed</c> e o dicionário por campo
    /// (<see cref="ValidationResult.ToDictionary"/>) serializado no trailer
    /// <see cref="ValidationErrorsTrailerKey"/>.
    /// </summary>
    public static RpcException ToValidationFailedException(this ValidationResult validationResult) =>
        ToValidationFailedException((IReadOnlyDictionary<string, string[]>)validationResult.ToDictionary());

    /// <summary>
    /// Mesma conversão de <see cref="ToValidationFailedException(ValidationResult)"/>,
    /// para um dicionário de erros já pronto.
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
/// gRPC dos RPCs da Fase 3.
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
