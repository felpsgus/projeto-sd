using System.Diagnostics;
using System.Security.Claims;
using Grpc.Core;
using Grpc.Core.Interceptors;
using TodoList.Gateway.Api.Authentication;

namespace TodoList.Gateway.Api.Backends;

/// <summary>
/// Interceptor de <b>cliente</b> (BE-36, D-34), registrado só no cliente
/// gRPC do Tasks (<c>.AddInterceptor&lt;ClientMetadataInterceptor&gt;()</c> em
/// <see cref="ServiceCollectionExtensions.AddBackendGrpcClients"/>) — nunca
/// no do Identity: <c>ValidateToken</c> roda dentro do próprio
/// <see cref="Authentication.IdentityTokenAuthenticationHandler"/>, antes de
/// existir um <see cref="ClaimsPrincipal"/> autenticado para extrair o claim
/// <c>sub</c>.
///
/// <para>
/// Acrescenta, em toda chamada de saída ao Tasks:
/// <list type="bullet">
/// <item><c>x-user-id</c> — o claim <c>sub</c> do usuário autenticado
/// (D-34), para que o Tasks saiba o dono da tarefa sem o Gateway repassar o
/// token;</item>
/// <item><c>x-client-date</c> — repassado do header <c>X-Client-Date</c> do
/// request HTTP de entrada, se houver (D-18) — mesmo contrato de header que
/// o Tasks já lia diretamente antes do Gateway existir.</item>
/// </list>
/// <c>traceparent</c> também é acrescentado aqui, explicitamente, a partir da
/// <see cref="Activity.Current"/> (CA-25) — a propagação automática do
/// <see cref="HttpClient"/> via <c>System.Net.Http.DiagnosticsHandler</c>
/// depende do handler HTTP concreto por trás do canal (não é garantida, por
/// exemplo, quando o handler primário é substituído por um em memória, como
/// nos testes de integração) — verificado pelo teste que confere o header
/// recebido pelo fake do Tasks. Mesmo padrão de
/// <c>GrpcIdentityGateway.BuildMetadata</c> no Tasks Service, usado aqui para
/// o cliente do Tasks.
/// </para>
/// </summary>
public sealed class ClientMetadataInterceptor : Interceptor
{
    /// <summary>Metadata gRPC com o dono autenticado da requisição (D-34).</summary>
    public const string UserIdHeaderName = "x-user-id";

    /// <summary>Metadata gRPC com a data local do usuário (D-18).</summary>
    public const string ClientDateHeaderName = "x-client-date";

    /// <summary>Header HTTP de entrada do qual <see cref="ClientDateHeaderName"/> é repassado (D-18).</summary>
    public const string ClientDateRequestHeaderName = "X-Client-Date";

    /// <summary>Metadata gRPC com o W3C Trace Context corrente (CA-25).</summary>
    public const string TraceparentHeaderName = "traceparent";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public ClientMetadataInterceptor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithHeaders(BuildHeaders(context.Options.Headers)));

        return continuation(request, newContext);
    }

    private Metadata BuildHeaders(Metadata? existing)
    {
        var metadata = new Metadata();

        if (existing is not null)
        {
            foreach (var entry in existing)
            {
                metadata.Add(entry);
            }
        }

        var httpContext = _httpContextAccessor.HttpContext;
        var userId = httpContext?.User.FindFirst(IdentityClaimTypes.Subject)?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            metadata.Add(UserIdHeaderName, userId);
        }

        var clientDate = httpContext?.Request.Headers[ClientDateRequestHeaderName].ToString();

        if (!string.IsNullOrEmpty(clientDate))
        {
            metadata.Add(ClientDateHeaderName, clientDate);
        }

        var traceId = Activity.Current?.Id;

        if (!string.IsNullOrEmpty(traceId))
        {
            metadata.Add(TraceparentHeaderName, traceId);
        }

        return metadata;
    }
}
