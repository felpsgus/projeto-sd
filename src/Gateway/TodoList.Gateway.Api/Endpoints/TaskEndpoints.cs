using TodoList.Gateway.Api.Backends;
using TodoList.Gateway.Api.Contracts;
using TodoList.Gateway.Api.Validation;

namespace TodoList.Gateway.Api.Endpoints;

/// <summary>
/// <c>POST /api/tasks</c> (BE-36, RN-TASK-02 a RN-TASK-07/RN-TASK-10) —
/// autenticado pela fallback policy padrão (nenhum <c>AllowAnonymous</c>
/// aqui); <see cref="ValidationFilter{TRequest}"/> roda antes de qualquer
/// chamada gRPC (CA-05/CA-06/CA-08). Sucesso devolve 201 com
/// <c>Location: /api/tasks/{id}</c> (CA-01) — o Gateway não expõe
/// <c>GET /api/tasks/{id}</c> nesta task; o <c>Location</c> só documenta onde
/// o recurso passaria a existir.
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
    }

    private static async Task<IResult> HandleCreateTaskAsync(
        CreateTaskHttpRequest request, ITasksBackend tasksBackend, CancellationToken cancellationToken)
    {
        var response = await tasksBackend.CreateTaskAsync(request, cancellationToken);

        return Results.Created($"/api/tasks/{response.Id}", response);
    }
}
