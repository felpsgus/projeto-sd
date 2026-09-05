using Microsoft.AspNetCore.Http;

namespace TodoList.Tasks.Api.Security;

/// <summary>
/// Filtro de endpoint (BE-29, CA-03) adicionado a <c>POST /api/tasks</c>
/// <b>somente</b> quando <c>Tasks:AllowAnonymousCreate=true</c> — garante que
/// o header <c>X-User-Id</c> existe e é um <see cref="Guid"/> válido
/// <b>antes</b> de qualquer código do caso de uso rodar (nenhuma chamada
/// gRPC gasta com um dono que nem chegou a ser identificado). Sem essa
/// garantia prévia, <see cref="HeaderCurrentUser.Id"/> não teria como
/// distinguir "header ausente" de "bug de configuração" sem lançar — e um
/// 400 claro é exatamente o que esta task pede, não um 500.
/// </summary>
public sealed class RequireValidUserIdHeaderFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var header = context.HttpContext.Request.Headers[HeaderCurrentUser.HeaderName].ToString();

        if (!Guid.TryParse(header, out _))
        {
            var errors = new Dictionary<string, string[]>
            {
                [HeaderCurrentUser.HeaderName] =
                [
                    $"O cabeçalho '{HeaderCurrentUser.HeaderName}' é obrigatório e deve ser um Guid válido " +
                    "enquanto o modo de criação anônima (Tasks:AllowAnonymousCreate=true) estiver ligado.",
                ],
            };

            return ValueTask.FromResult<object?>(Microsoft.AspNetCore.Http.Results.ValidationProblem(errors));
        }

        return next(context);
    }
}
