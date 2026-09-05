namespace TodoList.Tasks.Application.Identity;

/// <summary>
/// Sinaliza que a chamada gRPC ao Identity falhou por motivo de transporte
/// (serviço fora do ar, deadline excedido, canal indisponível) — nunca por
/// resposta de negócio negativa, que é <see cref="UserValidation"/> com
/// <c>Exists=false</c>.
///
/// <para>
/// Nota (BE-27): o BE-03 (<c>Result&lt;T&gt;</c>/<c>Error</c>) ainda não existe
/// nesta base de código. A especificação de BE-27 pede que a
/// <c>Grpc.Core.RpcException</c> seja traduzida, na borda da
/// <c>Infrastructure</c>, para o <c>Error</c> do catálogo do Tasks
/// (<c>identity.unavailable</c>) — sem esse catálogo, o mínimo que preserva a
/// intenção é esta exceção própria da <c>Application</c>, sem nenhum
/// vocabulário de transporte (nada de <c>Grpc.Core</c> aqui). O requisito duro
/// (CA-08 de BE-27) é que <c>RpcException</c> não atravesse a
/// <c>Infrastructure</c> — e não atravessa. Quando BE-03 existir, BE-28 passa
/// a capturar esta exceção no caso de uso e convertê-la num <c>Result</c> de
/// falha com o código <c>identity.unavailable</c> (D-28), em vez de deixá-la
/// subir como exceção até o endpoint.
/// </para>
/// </summary>
public sealed class IdentityUnavailableException : Exception
{
    public IdentityUnavailableException()
    {
    }

    public IdentityUnavailableException(string message)
        : base(message)
    {
    }

    public IdentityUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
