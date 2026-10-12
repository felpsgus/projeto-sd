using Grpc.Core;

namespace TodoList.Tasks.UnitTests.Grpc;

/// <summary>
/// <see cref="ServerCallContext"/> mínimo para testar interceptors/handlers
/// gRPC sem subir um servidor real — mesmo padrão de
/// <c>TodoList.Identity.UnitTests.FakeServerCallContext</c> (BE-26), com a
/// metadata de requisição injetável pelo teste (BE-35: os testes de
/// <c>RequireCallerIdentityInterceptor</c> precisam controlar
/// <c>x-user-id</c>).
/// </summary>
internal sealed class FakeServerCallContext : ServerCallContext
{
    public FakeServerCallContext(Metadata? requestHeaders = null)
    {
        RequestHeadersCore = requestHeaders ?? new Metadata();
    }

    protected override string MethodCore => "CreateTask";

    protected override string HostCore => "localhost";

    protected override string PeerCore => "in-process";

    protected override DateTime DeadlineCore => DateTime.MaxValue;

    protected override Metadata RequestHeadersCore { get; }

    protected override CancellationToken CancellationTokenCore => CancellationToken.None;

    protected override Metadata ResponseTrailersCore { get; } = new();

    protected override Status StatusCore { get; set; }

    protected override WriteOptions? WriteOptionsCore { get; set; }

    protected override AuthContext AuthContextCore { get; } = new("in-process", new Dictionary<string, List<AuthProperty>>());

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => null!;

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}
