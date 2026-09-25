using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TodoList.Gateway.Api.Authentication;
using TodoList.Gateway.Api.Backends;
using TodoList.Gateway.Api.Contracts;
using TodoList.Gateway.Api.Validation;

namespace TodoList.Gateway.Api.Endpoints;

/// <summary>
/// Endpoints de perfil e conta do usuário autenticado (BE-14/BE-15/BE-16):
/// <list type="bullet">
/// <item><c>GET /api/me</c> — perfil do usuário do token.</item>
/// <item><c>PATCH /api/me</c> — altera o nome de exibição
/// (<see cref="ValidationFilter{TRequest}"/> roda antes de qualquer chamada
/// gRPC; BE-14 CA-06/CA-07). Não <c>PUT</c> — o corpo não é uma substituição
/// completa do recurso, só o nome (BE-14).</item>
/// <item><c>POST /api/me/change-password</c> — sucesso é 204 sem corpo
/// (BE-15, CA-15).</item>
/// <item><c>DELETE /api/me</c> — exclusão irreversível da própria conta,
/// sucesso também 204 (BE-16).</item>
/// </list>
/// Nenhuma rota aqui tem <c>AllowAnonymous</c> — a fallback policy padrão do
/// Gateway já exige usuário autenticado. Em todo handler, o <c>user_id</c>
/// enviado ao Identity vem do claim <c>sub</c> do token
/// (<see cref="ClaimsPrincipalExtensions.GetUserId"/>), nunca do corpo ou da
/// query — não existe parâmetro de rota com id de usuário, então não há como
/// atingir outro perfil (BE-14, nota técnica).
/// </summary>
public sealed class UserEndpoints : IEndpointRouteHandler
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me", HandleGetMeAsync)
            .WithName("GetMe")
            .WithSummary("Consulta o perfil do usuário autenticado.")
            .WithTags("Users");

        endpoints.MapPatch("/api/me", HandlePatchMeAsync)
            .WithRequestValidation<UpdateProfileHttpRequest>()
            .WithName("UpdateMe")
            .WithSummary("Altera o nome de exibição do usuário autenticado (não altera o e-mail).")
            .WithTags("Users");

        endpoints.MapPost("/api/me/change-password", HandleChangePasswordAsync)
            .WithRequestValidation<ChangePasswordHttpRequest>()
            .WithName("ChangePassword")
            .WithSummary("Troca a senha do usuário autenticado, exigindo a senha atual.")
            .WithTags("Users");

        endpoints.MapDelete("/api/me", HandleDeleteMeAsync)
            .WithRequestValidation<DeleteAccountHttpRequest>()
            .WithName("DeleteMe")
            .WithSummary("Exclui a própria conta (operação irreversível), exigindo confirmação de senha.")
            .WithTags("Users");
    }

    private static async Task<IResult> HandleGetMeAsync(
        ClaimsPrincipal user, IIdentityBackend identityBackend, CancellationToken cancellationToken)
    {
        var profile = await identityBackend.GetProfileAsync(user.GetUserId(), cancellationToken);

        return Results.Ok(profile);
    }

    private static async Task<IResult> HandlePatchMeAsync(
        ClaimsPrincipal user, UpdateProfileHttpRequest request, IIdentityBackend identityBackend, CancellationToken cancellationToken)
    {
        var profile = await identityBackend.UpdateProfileAsync(user.GetUserId(), request.DisplayName, cancellationToken);

        return Results.Ok(profile);
    }

    /// <summary>
    /// BE-15 — senha atual incorreta chega como <see cref="Backends.BackendCallException"/>
    /// com <c>StatusCode.InvalidArgument</c> (decisão do tech lead: 400, não
    /// 401 — ver <c>AuthErrors.InvalidCurrentPassword</c> no Identity) e o
    /// <see cref="ErrorHandling.GrpcErrorMapping"/> já existente monta o
    /// <c>ValidationProblem</c> com <c>errors.currentPassword</c> e
    /// <c>errorCode: auth.invalid_current_password</c> — nenhum tratamento
    /// especial precisa existir aqui.
    /// </summary>
    private static async Task<IResult> HandleChangePasswordAsync(
        ClaimsPrincipal user, ChangePasswordHttpRequest request, IIdentityBackend identityBackend, CancellationToken cancellationToken)
    {
        await identityBackend.ChangePasswordAsync(user.GetUserId(), request.CurrentPassword, request.NewPassword, cancellationToken);

        return Results.NoContent();
    }

    /// <summary>
    /// BE-16 — mesmo tratamento de erro de <see cref="HandleChangePasswordAsync"/>
    /// para senha incorreta (<c>errors.password</c>). <c>[FromBody]</c> é
    /// explícito: Minimal API não infere corpo automaticamente em
    /// <c>DELETE</c> (só em <c>POST</c>/<c>PUT</c>/<c>PATCH</c>) — sem isto, o
    /// binder recusa a rota na inicialização ("Body was inferred but the
    /// method does not allow inferred body parameters").
    /// </summary>
    private static async Task<IResult> HandleDeleteMeAsync(
        ClaimsPrincipal user, [FromBody] DeleteAccountHttpRequest request, IIdentityBackend identityBackend, CancellationToken cancellationToken)
    {
        await identityBackend.DeleteAccountAsync(user.GetUserId(), request.Password, cancellationToken);

        return Results.NoContent();
    }
}
