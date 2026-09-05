using Grpc.Core;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// <see cref="ServerCallContext"/> mínimo para testar handlers gRPC sem subir
/// um servidor real. O pacote legado <c>Grpc.Core.Testing</c> não é compatível
/// com o `Grpc.Core.Api` moderno usado por BE-25/BE-26, daí este fake local.
/// </summary>
internal sealed class FakeServerCallContext : ServerCallContext
{
    private readonly CancellationToken _cancellationToken;

    public FakeServerCallContext(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
    }

    protected override string MethodCore => "ValidateUser";

    protected override string HostCore => "localhost";

    protected override string PeerCore => "in-process";

    protected override DateTime DeadlineCore => DateTime.MaxValue;

    protected override Metadata RequestHeadersCore { get; } = new();

    protected override CancellationToken CancellationTokenCore => _cancellationToken;

    protected override Metadata ResponseTrailersCore { get; } = new();

    protected override Status StatusCore { get; set; }

    protected override WriteOptions? WriteOptionsCore { get; set; }

    protected override AuthContext AuthContextCore { get; } = new("in-process", new Dictionary<string, List<AuthProperty>>());

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => null!;

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}
