using Grpc.Core;

namespace TodoList.Gateway.Api.Backends;

/// <summary>
/// Uma chamada gRPC ao Tasks falhou com um <see cref="StatusCode"/> de
/// negócio (não indisponibilidade) — carrega o <see cref="StatusCode"/> e os
/// trailers originais para que <see cref="ErrorHandling.GrpcErrorMapping"/>
/// traduza para o HTTP correspondente (D-35, CA-16/CA-17), preservando o
/// <c>error-code</c> e, quando houver, o <c>validation-errors</c> do trailer.
/// </summary>
public sealed class BackendCallException : Exception
{
    public BackendCallException(RpcException rpcException)
        : base(rpcException.Status.Detail, rpcException)
    {
        StatusCode = rpcException.StatusCode;
        Trailers = rpcException.Trailers;
    }

    public StatusCode StatusCode { get; }

    public Metadata Trailers { get; }
}
