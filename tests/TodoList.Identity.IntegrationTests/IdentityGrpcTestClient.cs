using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
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
    private static readonly Method<LoginRequest, LoginResponse> _loginMethod = CreateMethod<LoginRequest, LoginResponse>("Login");
    private static readonly Method<RefreshSessionRequest, RefreshSessionResponse> _refreshSessionMethod = CreateMethod<RefreshSessionRequest, RefreshSessionResponse>("RefreshSession");
    private static readonly Method<LogoutRequest, Empty> _logoutMethod = CreateMethod<LogoutRequest, Empty>("Logout");
    private static readonly Method<LogoutAllRequest, Empty> _logoutAllMethod = CreateMethod<LogoutAllRequest, Empty>("LogoutAll");
    private static readonly Method<RegisterRequest, RegisterResponse> _registerMethod = CreateMethod<RegisterRequest, RegisterResponse>("Register");
    private static readonly Method<GetProfileRequest, ProfileResponse> _getProfileMethod = CreateMethod<GetProfileRequest, ProfileResponse>("GetProfile");
    private static readonly Method<UpdateProfileRequest, ProfileResponse> _updateProfileMethod = CreateMethod<UpdateProfileRequest, ProfileResponse>("UpdateProfile");
    private static readonly Method<ChangePasswordRequest, Empty> _changePasswordMethod = CreateMethod<ChangePasswordRequest, Empty>("ChangePassword");
    private static readonly Method<DeleteAccountRequest, Empty> _deleteAccountMethod = CreateMethod<DeleteAccountRequest, Empty>("DeleteAccount");

    private readonly GrpcChannel _channel;
    private readonly CallInvoker _invoker;

    public IdentityGrpcTestClient(HttpMessageHandler httpHandler, Uri address)
    {
        _channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpHandler = httpHandler });
        _invoker = _channel.CreateCallInvoker();
    }

    public AsyncUnaryCall<ValidateUserResponse> ValidateUserAsync(ValidateUserRequest request) =>
        _invoker.AsyncUnaryCall(_validateUserMethod, host: null, new CallOptions(), request);

    public AsyncUnaryCall<LoginResponse> LoginAsync(LoginRequest request) =>
        _invoker.AsyncUnaryCall(_loginMethod, host: null, new CallOptions(), request);

    public AsyncUnaryCall<RefreshSessionResponse> RefreshSessionAsync(RefreshSessionRequest request) =>
        _invoker.AsyncUnaryCall(_refreshSessionMethod, host: null, new CallOptions(), request);

    public AsyncUnaryCall<Empty> LogoutAsync(LogoutRequest request) =>
        _invoker.AsyncUnaryCall(_logoutMethod, host: null, new CallOptions(), request);

    public AsyncUnaryCall<Empty> LogoutAllAsync(LogoutAllRequest request) =>
        _invoker.AsyncUnaryCall(_logoutAllMethod, host: null, new CallOptions(), request);

    public AsyncUnaryCall<RegisterResponse> RegisterAsync(RegisterRequest request) =>
        _invoker.AsyncUnaryCall(_registerMethod, host: null, new CallOptions(), request);

    public AsyncUnaryCall<ProfileResponse> GetProfileAsync(GetProfileRequest request) =>
        _invoker.AsyncUnaryCall(_getProfileMethod, host: null, new CallOptions(), request);

    public AsyncUnaryCall<ProfileResponse> UpdateProfileAsync(UpdateProfileRequest request) =>
        _invoker.AsyncUnaryCall(_updateProfileMethod, host: null, new CallOptions(), request);

    public AsyncUnaryCall<Empty> ChangePasswordAsync(ChangePasswordRequest request) =>
        _invoker.AsyncUnaryCall(_changePasswordMethod, host: null, new CallOptions(), request);

    public AsyncUnaryCall<Empty> DeleteAccountAsync(DeleteAccountRequest request) =>
        _invoker.AsyncUnaryCall(_deleteAccountMethod, host: null, new CallOptions(), request);

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
