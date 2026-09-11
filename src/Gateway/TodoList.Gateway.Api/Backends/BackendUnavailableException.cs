namespace TodoList.Gateway.Api.Backends;

/// <summary>
/// Um backend (Identity ou Tasks) não respondeu dentro do deadline, ou não
/// pôde ser alcançado (BE-36, D-28/CA-13/CA-24) — sempre vira 503 com
/// <c>Retry-After</c> no <see cref="ErrorHandling.GlobalExceptionHandler"/>,
/// nunca 401: "não consegui perguntar" não é "credencial inválida".
/// </summary>
public sealed class BackendUnavailableException : Exception
{
    public BackendUnavailableException(string backendName, Exception innerException)
        : base($"O backend '{backendName}' está indisponível.", innerException)
    {
        BackendName = backendName;
    }

    /// <summary>Nome do backend indisponível ("Identity" ou "Tasks"), só para log — nunca no corpo da resposta (CA-19).</summary>
    public string BackendName { get; }
}
