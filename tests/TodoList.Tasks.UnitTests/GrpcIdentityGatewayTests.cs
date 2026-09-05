using System.Diagnostics;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TodoList.Contracts.Identity.V1;
using TodoList.Tasks.Application.Identity;
using TodoList.Tasks.Infrastructure.Identity;
using Xunit;

namespace TodoList.Tasks.UnitTests;

/// <summary>
/// Testes de unidade de <see cref="GrpcIdentityGateway"/> com o cliente gerado
/// substituído por uma subclasse controlada — BE-27, CA-04, CA-05, CA-08. Não
/// há chamada de rede nem servidor real: só a tradução feita pela própria
/// classe é exercitada.
/// </summary>
public class GrpcIdentityGatewayTests
{
    private static readonly IdentityGrpcOptions _options = new() { GrpcAddress = "http://identity", GrpcTimeoutSeconds = 2 };

    [Fact] // CA-04
    public async Task ValidateUserAsync_UsuarioAtivo_RetornaUserValidationComDadosDaResposta()
    {
        var response = new ValidateUserResponse { Exists = true, Active = true, DisplayName = "Ada Lovelace" };
        var gateway = CreateGateway(new StubIdentityServiceClient(response));

        var result = await gateway.ValidateUserAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().Be(new UserValidation(Exists: true, Active: true, DisplayName: "Ada Lovelace"));
    }

    [Fact] // CA-05
    public async Task ValidateUserAsync_UsuarioInexistente_RetornaUserValidationNegativoSemExcecao()
    {
        var response = new ValidateUserResponse { Exists = false, Active = false, DisplayName = string.Empty };
        var gateway = CreateGateway(new StubIdentityServiceClient(response));

        var result = await gateway.ValidateUserAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().Be(new UserValidation(Exists: false, Active: false, DisplayName: string.Empty));
    }

    [Fact] // CA-08
    public async Task ValidateUserAsync_IdentityIndisponivel_TraduzRpcExceptionEmIdentityUnavailableException()
    {
        var rpcException = new RpcException(new Status(StatusCode.Unavailable, "Identity fora do ar"));
        var gateway = CreateGateway(new StubIdentityServiceClient(rpcException));

        var act = async () => await gateway.ValidateUserAsync(Guid.NewGuid(), CancellationToken.None);

        (await act.Should().ThrowAsync<IdentityUnavailableException>())
            .WithInnerException<RpcException>();
    }

    [Fact] // CA-08 — o requisito duro: RpcException não deve escapar da Infrastructure.
    public async Task ValidateUserAsync_IdentityIndisponivel_NuncaLancaRpcExceptionDiretamente()
    {
        var rpcException = new RpcException(new Status(StatusCode.DeadlineExceeded, "prazo excedido"));
        var gateway = CreateGateway(new StubIdentityServiceClient(rpcException));

        var act = async () => await gateway.ValidateUserAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().NotThrowAsync<RpcException>();
    }

    [Fact] // CA-10 — o CancellationToken da chamada é repassado ao gRPC.
    public async Task ValidateUserAsync_RepassaOCancellationTokenRecebidoParaAChamadaGrpc()
    {
        var response = new ValidateUserResponse { Exists = true, Active = true, DisplayName = "Ada Lovelace" };
        var client = new StubIdentityServiceClient(response);
        var gateway = CreateGateway(client);
        using var cts = new CancellationTokenSource();

        await gateway.ValidateUserAsync(Guid.NewGuid(), cts.Token);

        client.LastCallOptions!.Value.CancellationToken.Should().Be(cts.Token);
    }

    [Fact] // CA-13 — o traceId de Activity.Current vai como metadata "traceparent".
    public async Task ValidateUserAsync_ComActivityAtual_EnviaTraceparentNaMetadata()
    {
        var response = new ValidateUserResponse { Exists = true, Active = true, DisplayName = "Ada Lovelace" };
        var client = new StubIdentityServiceClient(response);
        var gateway = CreateGateway(client);

        using var activity = new Activity("teste-de-requisicao");
        activity.Start();
        try
        {
            await gateway.ValidateUserAsync(Guid.NewGuid(), CancellationToken.None);
        }
        finally
        {
            activity.Stop();
        }

        client.LastCallOptions!.Value.Headers!.GetValue("traceparent").Should().Be(activity.Id);
    }

    [Fact] // CA-12 — log com userId, StatusCode e duração.
    public async Task ValidateUserAsync_RegistraLogComUserIdStatusCodeEDuracao()
    {
        var response = new ValidateUserResponse { Exists = true, Active = true, DisplayName = "Ada Lovelace" };
        var logger = new CapturingLogger();
        var userId = Guid.NewGuid();
        var gateway = new GrpcIdentityGateway(new StubIdentityServiceClient(response), Options.Create(_options), logger);

        await gateway.ValidateUserAsync(userId, CancellationToken.None);

        logger.Messages.Should().ContainSingle(message =>
            message.Contains(userId.ToString(), StringComparison.Ordinal)
            && message.Contains(StatusCode.OK.ToString(), StringComparison.Ordinal)
            && message.Contains("durationMs", StringComparison.Ordinal));
    }

    private static GrpcIdentityGateway CreateGateway(IdentityService.IdentityServiceClient client) =>
        new(client, Options.Create(_options), NullLogger<GrpcIdentityGateway>.Instance);

    /// <summary>
    /// Subclasse do cliente gerado (construtor protegido, método virtual —
    /// exatamente o que o gRPC prevê para teste sem servidor real), devolvendo
    /// uma resposta ou uma <see cref="RpcException"/> fixas, sem rede.
    /// </summary>
    private sealed class StubIdentityServiceClient : IdentityService.IdentityServiceClient
    {
        private readonly ValidateUserResponse? _response;
        private readonly RpcException? _exception;

        public StubIdentityServiceClient(ValidateUserResponse response) => _response = response;

        public StubIdentityServiceClient(RpcException exception) => _exception = exception;

        public CallOptions? LastCallOptions { get; private set; }

        public override AsyncUnaryCall<ValidateUserResponse> ValidateUserAsync(ValidateUserRequest request, CallOptions options)
        {
            LastCallOptions = options;

            var status = _exception?.Status ?? Status.DefaultSuccess;
            var responseTask = _exception is null
                ? Task.FromResult(_response!)
                : Task.FromException<ValidateUserResponse>(_exception);

            return new AsyncUnaryCall<ValidateUserResponse>(
                responseTask,
                Task.FromResult(new Metadata()),
                () => status,
                () => new Metadata(),
                () => { });
        }
    }

    /// <summary>Captura as mensagens formatadas de log, sem depender de mocking.</summary>
    private sealed class CapturingLogger : ILogger<GrpcIdentityGateway>
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages => _messages;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _messages.Add(formatter(state, exception));
    }
}
