using Google.Protobuf;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.Client;
using TodoList.Contracts.Tasks.V1;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// Cliente gRPC mínimo para os testes de integração de <c>CreateTask</c>
/// (BE-35) — mesmo padrão de
/// <c>TodoList.Identity.IntegrationTests.IdentityGrpcTestClient</c>: como
/// <c>tasks.proto</c> é compilado com <c>GrpcServices="Server"</c> só no
/// <c>TodoList.Tasks.Api</c> (D-29), não existe stub de cliente gerado — o RPC
/// é invocado direto pelo <see cref="CallInvoker"/>, com os mesmos tipos de
/// mensagem gerados para o servidor. O health check usa o cliente já pronto
/// de <c>Grpc.HealthCheck</c> (<see cref="Health.HealthClient"/>), trazido
/// transitivamente por <c>Grpc.AspNetCore.HealthChecks</c> (BE-35, D-37).
/// </summary>
internal sealed class TasksGrpcTestClient : IDisposable
{
    /// <summary>Nome da metadata gRPC que carrega o dono da tarefa (D-34).</summary>
    public const string OwnerHeaderName = "x-user-id";

    /// <summary>Nome da metadata gRPC com a data local do usuário (D-18/D-34).</summary>
    public const string ClientDateHeaderName = "x-client-date";

    private static readonly Method<CreateTaskRequest, TaskReply> _createTaskMethod =
        CreateMethod<CreateTaskRequest, TaskReply>("CreateTask");

    private readonly GrpcChannel _channel;
    private readonly CallInvoker _invoker;
    private readonly Health.HealthClient _healthClient;

    public TasksGrpcTestClient(HttpMessageHandler httpHandler, Uri address)
    {
        _channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpHandler = httpHandler });
        _invoker = _channel.CreateCallInvoker();
        _healthClient = new Health.HealthClient(_channel);
    }

    /// <summary>Monta a metadata <c>x-user-id</c> (e opcionalmente <c>x-client-date</c>) de uma chamada.</summary>
    public static Metadata OwnerHeaders(string? ownerId, string? clientDate = null)
    {
        var metadata = new Metadata();

        if (ownerId is not null)
        {
            metadata.Add(OwnerHeaderName, ownerId);
        }

        if (clientDate is not null)
        {
            metadata.Add(ClientDateHeaderName, clientDate);
        }

        return metadata;
    }

    public AsyncUnaryCall<TaskReply> CreateTaskAsync(
        CreateTaskRequest request, Metadata? headers = null, DateTime? deadline = null) =>
        _invoker.AsyncUnaryCall(
            _createTaskMethod, host: null, new CallOptions(headers: headers ?? new Metadata(), deadline: deadline), request);

    /// <summary>gRPC Health Checking Protocol (BE-35, D-37, CA-14) — sem <c>service</c> específico, o mesmo que o probe do Cloud Run consulta.</summary>
    public AsyncUnaryCall<HealthCheckResponse> CheckHealthAsync() =>
        _healthClient.CheckAsync(new HealthCheckRequest());

    public void Dispose() => _channel.Dispose();

    private static Method<TRequest, TResponse> CreateMethod<TRequest, TResponse>(string name)
        where TRequest : IMessage<TRequest>, new()
        where TResponse : IMessage<TResponse>, new() =>
        new(
            MethodType.Unary,
            "tasks.v1.TasksService",
            name,
            Marshallers.Create<TRequest>(static message => message.ToByteArray(), CreateParser<TRequest>()),
            Marshallers.Create<TResponse>(static message => message.ToByteArray(), CreateParser<TResponse>()));

    private static Func<byte[], T> CreateParser<T>()
        where T : IMessage<T>, new() =>
        bytes =>
        {
            var message = new T();
            message.MergeFrom(bytes);
            return message;
        };
}
