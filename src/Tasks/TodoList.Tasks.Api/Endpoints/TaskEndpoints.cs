using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TodoList.Tasks.Api.Configuration;
using TodoList.Tasks.Api.ResultMapping;
using TodoList.Tasks.Api.Security;
using TodoList.Tasks.Api.Validation;
using TodoList.Tasks.Application.Security;
using TodoList.Tasks.Application.Tasks;

namespace TodoList.Tasks.Api.Endpoints;

/// <summary>
/// <c>POST /api/tasks</c> (BE-17, BE-28, BE-29) — Minimal API, <b>não</b>
/// controller. O prefixo é sempre <c>/api</c> (nota técnica de BE-29): abrir
/// exceção aqui criaria uma rota que precisaria ser renomeada quando o API
/// Gateway (D-32) chegar.
/// </summary>
public sealed class TaskEndpoints : IEndpointRouteHandler
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/tasks").WithTags("Tasks");

        var route = group.MapPost("/", CreateTaskAsync)
            .WithName("CreateTask")
            .WithSummary("Cria uma nova tarefa para o usuário corrente (BE-17, BE-28, BE-29)")
            .WithRequestValidation<CreateTaskRequest>();

        var creationOptions = endpoints.ServiceProvider.GetRequiredService<IOptions<TasksCreationOptions>>().Value;

        if (creationOptions.AllowAnonymousCreate)
        {
            // TODO(dono: time Backend — BE-13; prazo: antes de qualquer ambiente
            // exposto fora de máquina local/demo): remover este ramo condicional
            // (allowlist de AllowAnonymous() + RequireValidUserIdHeaderFilter)
            // assim que a autenticação real do Tasks Service (BE-13) estiver
            // pronta. Ver BE-29 (D-30, "data de morte"): o sinal de que isso
            // aconteceu é este endpoint sair da allowlist do teste de guarda de
            // rotas equivalente (BE-13, CA-07/CA-09c).
            //
            // Allowlist justificada (BE-13, CA-07; BE-29, CA-12): este endpoint
            // só entra como AllowAnonymous quando Tasks:AllowAnonymousCreate=true
            // — um risco assumido e delimitado (nota técnica de BE-29), nunca o
            // padrão (Tasks:AllowAnonymousCreate=false).
            route
                .AllowAnonymous()
                .AddEndpointFilter<RequireValidUserIdHeaderFilter>();
        }
    }

    private static async Task<IResult> CreateTaskAsync(
        CreateTaskRequest request,
        CreateTaskHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.IsSuccess
            ? Microsoft.AspNetCore.Http.Results.Created($"/api/tasks/{result.Value.Id}", result.Value)
            : result.ToHttpResult();
    }
}
