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
    /// <summary>Dado e-mail/senha, devolve (succeeded?, accessToken, expiresAt, userId) — ou lança para simular indisponibilidade.</summary>
    public Func<string, string, (bool Succeeded, string AccessToken, DateTimeOffset ExpiresAt, string UserId)>? LoginHandler { get; set; }

    /// <summary>
    /// Dado (email, password, displayName), devolve o perfil criado — ou lança
    /// <see cref="RpcException"/> para simular e-mail duplicado (BE-07,
    /// FailedPrecondition) ou senha fora da política (InvalidArgument).
    /// </summary>
    public Func<string, string, string, (string Id, string Email, string DisplayName, DateTimeOffset CreatedAt)>? RegisterHandler { get; set; }

    /// <summary>Dado o <c>user_id</c> recebido, devolve o perfil — ou lança <see cref="RpcException"/>.</summary>
    public Func<string, (string Id, string Email, string DisplayName, DateTimeOffset CreatedAt)>? GetProfileHandler { get; set; }

    /// <summary>Dado (userId, displayName), devolve o perfil atualizado — ou lança <see cref="RpcException"/>.</summary>
    public Func<string, string, (string Id, string Email, string DisplayName, DateTimeOffset CreatedAt)>? UpdateProfileHandler { get; set; }

    /// <summary>
    /// Dado (userId, currentPassword, newPassword), <see langword="null"/>
    /// devolvido = sucesso; lança <see cref="RpcException"/> para simular
    /// falha (ex.: senha atual incorreta, decisão do tech lead: InvalidArgument
    /// com trailers error-code/validation-errors).
    /// </summary>
    public Action<string, string, string>? ChangePasswordHandler { get; set; }

    /// <summary>Dado (userId, password), sem retorno = sucesso; lança <see cref="RpcException"/> para simular falha.</summary>
    public Action<string, string>? DeleteAccountHandler { get; set; }

    /// <summary>Última chamada recebida por cada RPC novo (BE-36 CA-26 — verificação de que o user_id é o do token, não do corpo/query).</summary>
    public string? LastGetProfileUserId { get; private set; }

    public string? LastUpdateProfileUserId { get; private set; }

    public string? LastChangePasswordUserId { get; private set; }

    public string? LastDeleteAccountUserId { get; private set; }

    public int LoginCallCount { get; private set; }

    public int RegisterCallCount { get; private set; }

    public int GetProfileCallCount { get; private set; }

    public int UpdateProfileCallCount { get; private set; }

    public int ChangePasswordCallCount { get; private set; }

    public int DeleteAccountCallCount { get; private set; }

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

    public override Task<RegisterResponse> Register(RegisterRequest request, ServerCallContext context)
    {
        RegisterCallCount++;

        if (RegisterHandler is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "FakeIdentityService.RegisterHandler não configurado."));
        }

        var (id, email, displayName, createdAt) = RegisterHandler(request.Email, request.Password, request.DisplayName);

        return Task.FromResult(new RegisterResponse
        {
            Id = id,
            Email = email,
            DisplayName = displayName,
            CreatedAt = Timestamp.FromDateTimeOffset(createdAt),
        });
    }

    public override Task<ProfileResponse> GetProfile(GetProfileRequest request, ServerCallContext context)
    {
        GetProfileCallCount++;
        LastGetProfileUserId = request.UserId;

        if (GetProfileHandler is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "FakeIdentityService.GetProfileHandler não configurado."));
        }

        var (id, email, displayName, createdAt) = GetProfileHandler(request.UserId);

        return Task.FromResult(new ProfileResponse
        {
            Id = id,
            Email = email,
            DisplayName = displayName,
            CreatedAt = Timestamp.FromDateTimeOffset(createdAt),
        });
    }

    public override Task<ProfileResponse> UpdateProfile(UpdateProfileRequest request, ServerCallContext context)
    {
        UpdateProfileCallCount++;
        LastUpdateProfileUserId = request.UserId;

        if (UpdateProfileHandler is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "FakeIdentityService.UpdateProfileHandler não configurado."));
        }

        var (id, email, displayName, createdAt) = UpdateProfileHandler(request.UserId, request.DisplayName);

        return Task.FromResult(new ProfileResponse
        {
            Id = id,
            Email = email,
            DisplayName = displayName,
            CreatedAt = Timestamp.FromDateTimeOffset(createdAt),
        });
    }

    public override Task<Empty> ChangePassword(ChangePasswordRequest request, ServerCallContext context)
    {
        ChangePasswordCallCount++;
        LastChangePasswordUserId = request.UserId;

        if (ChangePasswordHandler is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "FakeIdentityService.ChangePasswordHandler não configurado."));
        }

        ChangePasswordHandler(request.UserId, request.CurrentPassword, request.NewPassword);

        return Task.FromResult(new Empty());
    }

    public override Task<Empty> DeleteAccount(DeleteAccountRequest request, ServerCallContext context)
    {
        DeleteAccountCallCount++;
        LastDeleteAccountUserId = request.UserId;

        if (DeleteAccountHandler is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "FakeIdentityService.DeleteAccountHandler não configurado."));
        }

        DeleteAccountHandler(request.UserId, request.Password);

        return Task.FromResult(new Empty());
    }
}
