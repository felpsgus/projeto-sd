using Grpc.Core;
using Grpc.Core.Interceptors;
using TodoList.Tasks.Api.Security;

namespace TodoList.Tasks.Api.Grpc;

/// <summary>
/// Interceptor de servidor (BE-35, D-34) que substitui
/// <c>RequireValidUserIdHeaderFilter</c> (BE-29, removido): rejeita a chamada
/// com <see cref="StatusCode.Unauthenticated"/> antes de qualquer RPC de
/// <c>TasksGrpcService</c> ser invocado, se a metadata
/// <see cref="CallerIdentityCurrentUser.HeaderName"/> estiver ausente ou não
/// for um <see cref="Guid"/> válido (CA-08, CA-09) — nenhuma chamada ao
/// Identity acontece nesse caminho.
///
/// <para>
/// <b>Por que interceptor, e não filtro de endpoint como antes (BE-29).</b>
/// gRPC no ASP.NET Core não usa endpoint filters de Minimal API da mesma
/// forma; a extensibilidade idiomática do lado servidor é
/// <see cref="Interceptor"/> — equivalente funcional do filtro que ele
/// substitui, mas no mecanismo correto do transporte (nota técnica de BE-35).
/// </para>
///
/// <para>
/// <b>Armadilha evitada (BE-35):</b> este interceptor é registrado só para
/// <c>TasksGrpcService</c>, via
/// <c>AddGrpc().AddServiceOptions&lt;TasksGrpcService&gt;(o =&gt; o.Interceptors.Add&lt;RequireCallerIdentityInterceptor&gt;())</c>
/// em <c>Program.cs</c> — nunca globalmente. Registrado globalmente, ele
/// bloquearia <c>grpc.health.v1.Health/Check</c> (D-37), que não manda
/// <c>x-user-id</c> — o probe do Cloud Run não autentica.
/// </para>
/// </summary>
public sealed class RequireCallerIdentityInterceptor : Interceptor
{
    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var header = context.RequestHeaders.GetValue(CallerIdentityCurrentUser.HeaderName);

        if (!Guid.TryParse(header, out _))
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                $"A metadata '{CallerIdentityCurrentUser.HeaderName}' é obrigatória e deve ser um Guid válido."));
        }

        return continuation(request, context);
    }
}
