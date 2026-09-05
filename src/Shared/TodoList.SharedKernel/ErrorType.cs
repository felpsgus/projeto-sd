namespace TodoList.SharedKernel;

/// <summary>
/// Categoria de um <see cref="Error"/> — a única informação que o mecanismo de
/// tradução para HTTP (duplicado em cada <c>Api</c>, BE-03) precisa para decidir
/// o status code, sem que o <c>SharedKernel</c> conheça ASP.NET Core.
/// </summary>
public enum ErrorType
{
    /// <summary>Request malformado ou campo inválido. HTTP 400.</summary>
    Validation,

    /// <summary>Credencial ausente ou inválida. HTTP 401.</summary>
    Unauthorized,

    /// <summary>Identidade válida, mas sem permissão para o recurso. HTTP 403.</summary>
    Forbidden,

    /// <summary>Recurso inexistente. HTTP 404.</summary>
    NotFound,

    /// <summary>Conflito com o estado atual do recurso. HTTP 409.</summary>
    Conflict,

    /// <summary>Limite de taxa excedido. HTTP 429.</summary>
    TooManyRequests,

    /// <summary>
    /// Dependência externa temporariamente inalcançável (ex.: D-28 — Identity
    /// fora do ar). HTTP 503. Acrescentado além da tabela original de BE-03
    /// porque BE-28 (validação de dono via gRPC) exige o código
    /// <c>identity.unavailable</c> mapeado para 503.
    /// </summary>
    Unavailable,

    /// <summary>Falha técnica não classificada. HTTP 500.</summary>
    Failure,
}
