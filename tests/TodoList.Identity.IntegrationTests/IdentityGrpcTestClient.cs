using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using TodoList.Contracts.Identity.V1;

namespace TodoList.Identity.IntegrationTests;

/// <summary>
/// Cliente gRPC mínimo para os testes de integração. Não regenera o .proto
/// (evitaria duplicar os tipos de mensagem já gerados no lado servidor,
/// referenciado via <c>TodoList.Identity.Api</c>) — em vez disso invoca o RPC
/// diretamente pelo <see cref="CallInvoker"/>, com os mesmos tipos de mensagem
/// gerados para o servidor (BE-25, CA-02: os dois lados vêm do mesmo .proto).
/// </summary>
internal sealed class IdentityGrpcTestClient : IDisposable
{
    private static readonly Method<ValidateUserRequest, ValidateUserResponse> _validateUserMethod = CreateMethod<ValidateUserRequest, ValidateUserResponse>("ValidateUser");
    private static readonly Method<ValidateTokenRequest, ValidateTokenResponse> _validateTokenMethod = CreateMethod<ValidateTokenRequest, ValidateTokenResponse>("ValidateToken");
    private static readonly Method<LoginRequest, LoginResponse> _loginMethod = CreateMethod<LoginRequest, LoginResponse>("Login");

    private readonly GrpcChannel _channel;
    private readonly CallInvoker _invoker;

    public IdentityGrpcTestClient(HttpMessageHandler httpHandler, Uri address)
    {
        _channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpHandler = httpHandler });
        _invoker = _channel.CreateCallInvoker();
    }

    public AsyncUnaryCall<ValidateUserResponse> ValidateUserAsync(ValidateUserRequest request) =>
        _invoker.AsyncUnaryCall(_validateUserMethod, host: null, new CallOptions(), request);

    public AsyncUnaryCall<ValidateTokenResponse> ValidateTokenAsync(ValidateTokenRequest request) =>
        _invoker.AsyncUnaryCall(_validateTokenMethod, host: null, new CallOptions(), request);

    public AsyncUnaryCall<LoginResponse> LoginAsync(LoginRequest request) =>
        _invoker.AsyncUnaryCall(_loginMethod, host: null, new CallOptions(), request);

    public void Dispose() => _channel.Dispose();

    private static Method<TRequest, TResponse> CreateMethod<TRequest, TResponse>(string name)
        where TRequest : IMessage<TRequest>, new()
        where TResponse : IMessage<TResponse>, new() =>
        new(
            MethodType.Unary,
            "identity.v1.IdentityService",
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
