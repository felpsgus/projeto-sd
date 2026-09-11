using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using TodoList.Contracts.Identity.V1;

namespace TodoList.Gateway.IntegrationTests.Fakes;

/// <summary>
/// Dublê in-process do Identity Service (BE-36). Exposto só por POCOs/<see cref="Func{T,TResult}"/>
/// — <see cref="TodoList.Gateway.IntegrationTests"/> nunca precisa nomear um
/// tipo de mensagem gerado pelo proto, evitando qualquer ambiguidade com o
/// lado cliente gerado em <c>TodoList.Gateway.Api</c>.
/// </summary>
public sealed class FakeIdentityService : IdentityService.IdentityServiceBase
{
    /// <summary>Dado o access token recebido, devolve (válido?, userId) — ou lança para simular indisponibilidade.</summary>
    public Func<string, (bool Valid, string UserId)>? ValidateTokenHandler { get; set; }

    /// <summary>Dado e-mail/senha, devolve (succeeded?, accessToken, expiresAt, userId) — ou lança para simular indisponibilidade.</summary>
    public Func<string, string, (bool Succeeded, string AccessToken, DateTimeOffset ExpiresAt, string UserId)>? LoginHandler { get; set; }

    /// <summary>Metadata recebida na última chamada a <see cref="ValidateToken"/> (CA-25: verificação do <c>traceparent</c>).</summary>
    public Metadata? LastValidateTokenRequestHeaders { get; private set; }

    public int ValidateTokenCallCount { get; private set; }

    public int LoginCallCount { get; private set; }

    public override Task<ValidateTokenResponse> ValidateToken(ValidateTokenRequest request, ServerCallContext context)
    {
        ValidateTokenCallCount++;
        LastValidateTokenRequestHeaders = context.RequestHeaders;

        if (ValidateTokenHandler is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "FakeIdentityService.ValidateTokenHandler não configurado."));
        }

        var (valid, userId) = ValidateTokenHandler(request.AccessToken);

        return Task.FromResult(new ValidateTokenResponse { Valid = valid, UserId = userId ?? string.Empty });
    }

    public override Task<LoginResponse> Login(LoginRequest request, ServerCallContext context)
    {
        LoginCallCount++;

        if (LoginHandler is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "FakeIdentityService.LoginHandler não configurado."));
        }

        var (succeeded, accessToken, expiresAt, userId) = LoginHandler(request.Email, request.Password);

        var response = new LoginResponse { Succeeded = succeeded };

        if (succeeded)
        {
            response.AccessToken = accessToken;
            response.ExpiresAt = Timestamp.FromDateTimeOffset(expiresAt);
            response.UserId = userId;
        }

        return Task.FromResult(response);
    }
}
