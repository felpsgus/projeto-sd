using Microsoft.Extensions.Options;
using TodoList.Gateway.Api.Backends;
using TodoList.Gateway.Api.Configuration;
using TodoList.Gateway.Api.Contracts;
using TodoList.Gateway.Api.Validation;

namespace TodoList.Gateway.Api.Endpoints;

/// <summary>
/// Endpoints de tarefas (BE-36/BE-41), todos autenticados pela fallback
/// policy padrão (nenhum <c>AllowAnonymous</c> aqui):
/// <list type="bullet">
/// <item><c>POST /api/tasks</c> — <see cref="ValidationFilter{TRequest}"/>
/// roda antes de qualquer chamada gRPC (CA-05/CA-06/CA-08). Sucesso devolve
/// 201 com <c>Location: /api/tasks/{id}</c> (CA-01), que agora resolve de
/// fato num <c>GET</c> subsequente (BE-41, CA-23) — nenhuma mudança neste
/// handler além do comentário.</item>
/// <item><c>GET /api/tasks</c> — <see cref="HandleListTasksAsync"/> valida
/// <c>page</c>/<c>pageSize</c> na borda (<see cref="ListTasksQueryValidator"/>,
/// CA-19) antes de qualquer chamada ao Tasks.</item>
/// <item><c>GET /api/tasks/{id}</c> — <see cref="HandleGetTaskAsync"/> recebe
/// <c>id</c> como <c>string</c> (nunca <c>{id:guid}</c> na rota, CA-22: com a
/// restrição, um id malformado casaria com "rota não encontrada" e viraria
/// 404, não 400) e valida o formato antes de qualquer chamada gRPC.</item>
/// </list>
/// </summary>
public sealed class TaskEndpoints : IEndpointRouteHandler
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/tasks", HandleCreateTaskAsync)
            .WithRequestValidation<CreateTaskHttpRequest>()
            .WithName("CreateTask")
            .WithSummary("Cria uma tarefa em nome do usuário autenticado.")
            .WithTags("Tasks");

        endpoints.MapGet("/api/tasks", HandleListTasksAsync)
            .WithName("ListTasks")
            .WithSummary("Lista as tarefas do usuário autenticado, paginadas (mais recentes primeiro).")
            .WithTags("Tasks");

        endpoints.MapGet("/api/tasks/{id}", HandleGetTaskAsync)
            .WithName("GetTask")
            .WithSummary("Consulta uma tarefa específica do usuário autenticado.")
            .WithTags("Tasks");
    }

    private static async Task<IResult> HandleCreateTaskAsync(
        CreateTaskHttpRequest request, ITasksBackend tasksBackend, CancellationToken cancellationToken)
    {
        var response = await tasksBackend.CreateTaskAsync(request, cancellationToken);

        return Results.Created($"/api/tasks/{response.Id}", response);
    }

    /// <summary>
    /// BE-41, CA-17/CA-19 — <c>page</c>/<c>pageSize</c> ausentes recebem os
    /// padrões de <see cref="PagingOptions"/> (a cópia própria do Gateway,
    /// D-33); informados fora da faixa (page &lt; 1, pageSize fora de
    /// 1..MaxPageSize) viram 400 sem nenhuma chamada gRPC ao Tasks.
    /// </summary>
    private static async Task<IResult> HandleListTasksAsync(
        int? page,
        int? pageSize,
        ITasksBackend tasksBackend,
        IOptions<PagingOptions> pagingOptions,
        CancellationToken cancellationToken)
    {
        var options = pagingOptions.Value;
        var errors = ListTasksQueryValidator.Validate(page, pageSize, options);

        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        var request = new ListTasksHttpRequest(page ?? 1, pageSize ?? options.DefaultPageSize);
        var response = await tasksBackend.ListTasksAsync(request, cancellationToken);

        return Results.Ok(response);
    }

    /// <summary>
    /// BE-41, CA-20 a CA-22 — <paramref name="id"/> fora do formato de
    /// <see cref="Guid"/> vira 400 sem round-trip gRPC (CA-22); um
    /// <see cref="Grpc.Core.StatusCode.NotFound"/> do Tasks (tarefa
    /// inexistente ou alheia, RN-AUTZ-03) é traduzido a 404 pelo
    /// <see cref="ErrorHandling.GrpcErrorMapping"/> já existente, com corpo
    /// idêntico para os dois casos (CA-21) — este endpoint não distingue.
    /// </summary>
    private static async Task<IResult> HandleGetTaskAsync(
        string id, ITasksBackend tasksBackend, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out _))
        {
            var errors = new Dictionary<string, string[]>
            {
                ["id"] = ["O id da tarefa deve ser um Guid válido."],
            };

            return Results.ValidationProblem(errors);
        }

        var response = await tasksBackend.GetTaskAsync(id, cancellationToken);

        return Results.Ok(response);
    }
}
