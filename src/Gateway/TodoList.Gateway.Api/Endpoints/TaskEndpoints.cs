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
/// <item><c>PUT /api/tasks/{id}</c> — <see cref="HandleUpdateTaskAsync"/>
/// (BE-19): mesma checagem de formato de id de <see cref="HandleGetTaskAsync"/>,
/// mais <see cref="ValidationFilter{TRequest}"/> sobre o corpo (mesma
/// validação de <c>POST</c>). Sucesso devolve 200 com o <c>TaskResponse</c>
/// atualizado — semântica de substituição completa (BE-19, nota técnica).</item>
/// <item><c>POST /api/tasks/{id}/complete</c> e <c>POST /api/tasks/{id}/reopen</c>
/// — <see cref="HandleCompleteTaskAsync"/>/<see cref="HandleReopenTaskAsync"/>
/// (BE-20): sem corpo de requisição; transição inválida vira 409 pelo mesmo
/// <see cref="ErrorHandling.GrpcErrorMapping"/> (D-35), sem tratamento
/// especial aqui.</item>
/// <item><c>DELETE /api/tasks/{id}</c> — <see cref="HandleDeleteTaskAsync"/>
/// (BE-21): soft delete no Tasks, 204 sem corpo na borda.</item>
/// </list>
/// </summary>
public sealed class TaskEndpoints : IEndpointRouteHandler
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/tasks", HandleCreateTaskAsync)
            .WithRequestValidation<CreateTaskHttpRequest>()
            .WithName("CreateTask")
            .WithDescription("Título obrigatório; prioridade, descrição e vencimento opcionais. Responde 201 com Location. 404/409 quando o dono é inexistente/inativo ou o limite de tarefas ativas foi atingido; 503 se o Identity estiver fora do ar (a tarefa não é criada).")
            .Produces<TaskHttpResponse>(StatusCodes.Status201Created)
            .ProducesProblems(StatusCodes.Status400BadRequest, StatusCodes.Status401Unauthorized, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict, StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Cria uma tarefa em nome do usuário autenticado.")
            .WithTags("Tasks");

        endpoints.MapGet("/api/tasks", HandleListTasksAsync)
            .WithName("ListTasks")
            .WithDescription("Paginada (page, pageSize), com filtros opcionais status, priority (repetível), overdue e search. Só as tarefas do usuário autenticado, mais recentes primeiro.")
            .Produces<ListTasksHttpResponse>(StatusCodes.Status200OK)
            .ProducesProblems(StatusCodes.Status400BadRequest, StatusCodes.Status401Unauthorized, StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Lista as tarefas do usuário autenticado, paginadas (mais recentes primeiro).")
            .WithTags("Tasks");

        endpoints.MapGet("/api/tasks/{id}", HandleGetTaskAsync)
            .WithName("GetTask")
            .WithDescription("Tarefa inexistente, removida ou de outro usuário é sempre o mesmo 404 (RN-AUTZ-03). Id que não é Guid é 400.")
            .Produces<TaskHttpResponse>(StatusCodes.Status200OK)
            .ProducesProblems(StatusCodes.Status400BadRequest, StatusCodes.Status401Unauthorized, StatusCodes.Status404NotFound, StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Consulta uma tarefa específica do usuário autenticado.")
            .WithTags("Tasks");

        endpoints.MapPut("/api/tasks/{id}", HandleUpdateTaskAsync)
            .WithRequestValidation<UpdateTaskHttpRequest>()
            .WithName("UpdateTask")
            .WithDescription("PUT com semântica de SUBSTITUIÇÃO (ADR 0008): campos omitidos viram null (description, dueDate) e a prioridade volta a Medium - para manter um valor, reenvie-o. Enviar status, id ou ownerId no corpo é ignorado; tarefa concluída pode ser editada e continua concluída.")
            .Produces<TaskHttpResponse>(StatusCodes.Status200OK)
            .ProducesProblems(StatusCodes.Status400BadRequest, StatusCodes.Status401Unauthorized, StatusCodes.Status404NotFound, StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Substitui título, descrição, prioridade e vencimento de uma tarefa própria.")
            .WithTags("Tasks");

        endpoints.MapPost("/api/tasks/{id}/complete", HandleCompleteTaskAsync)
            .WithName("CompleteTask")
            .WithDescription("Conclui uma tarefa pendente. Tarefa já concluída é 409 (task.already_completed), distinguível do 404 de tarefa alheia.")
            .Produces<TaskHttpResponse>(StatusCodes.Status200OK)
            .ProducesProblems(StatusCodes.Status400BadRequest, StatusCodes.Status401Unauthorized, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict, StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Conclui uma tarefa própria pendente.")
            .WithTags("Tasks");

        endpoints.MapPost("/api/tasks/{id}/reopen", HandleReopenTaskAsync)
            .WithName("ReopenTask")
            .WithDescription("Reabre uma tarefa concluída. Tarefa pendente é 409 (task.not_completed); também é 409 se reabrir estourar o limite de tarefas ativas.")
            .Produces<TaskHttpResponse>(StatusCodes.Status200OK)
            .ProducesProblems(StatusCodes.Status400BadRequest, StatusCodes.Status401Unauthorized, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict, StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Reabre uma tarefa própria concluída.")
            .WithTags("Tasks");

        endpoints.MapDelete("/api/tasks/{id}", HandleDeleteTaskAsync)
            .WithName("DeleteTask")
            .WithDescription("Soft delete: a tarefa some das listagens e é expurgada depois do período de retenção (ADR 0003). 204 sem corpo.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblems(StatusCodes.Status400BadRequest, StatusCodes.Status401Unauthorized, StatusCodes.Status404NotFound, StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Remove (soft delete) uma tarefa própria.")
            .WithTags("Tasks");
    }

    private static async Task<IResult> HandleCreateTaskAsync(
        CreateTaskHttpRequest request, ITasksBackend tasksBackend, CancellationToken cancellationToken)
    {
        var response = await tasksBackend.CreateTaskAsync(request, cancellationToken);

        return Results.Created($"/api/tasks/{response.Id}", response);
    }

    /// <summary>
    /// BE-41 CA-17/CA-19; BE-22 CA-05 a CA-17 — <c>page</c>/<c>pageSize</c>
    /// ausentes recebem os padrões de <see cref="PagingOptions"/> (a cópia
    /// própria do Gateway, D-33); <c>status</c>, <c>priority</c> (repetível,
    /// ex.: <c>priority=low&amp;priority=high</c>) e <c>overdue</c> chegam
    /// como <c>string</c>/<c>string[]</c> crus, nunca um tipo forte no
    /// parâmetro — o mesmo motivo de <see cref="CreateTaskHttpRequest"/>:
    /// um valor fora do vocabulário precisa virar erro por campo, não uma
    /// falha de binding sem detalhe. Qualquer parâmetro informado fora do
    /// vocabulário/faixa vira 400 sem nenhuma chamada gRPC ao Tasks (CA-11).
    /// </summary>
    private static async Task<IResult> HandleListTasksAsync(
        int? page,
        int? pageSize,
        string? status,
        string[]? priority,
        string? overdue,
        string? search,
        ITasksBackend tasksBackend,
        IOptions<PagingOptions> pagingOptions,
        CancellationToken cancellationToken)
    {
        var options = pagingOptions.Value;
        var validation = ListTasksQueryValidator.Validate(page, pageSize, status, priority, overdue, search, options);

        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.Errors!);
        }

        var response = await tasksBackend.ListTasksAsync(validation.Request!, cancellationToken);

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

    /// <summary>
    /// BE-19 — <paramref name="id"/> fora do formato de <see cref="Guid"/>
    /// vira 400 sem round-trip gRPC, mesmo padrão de
    /// <see cref="HandleGetTaskAsync"/> (a rota não usa <c>{id:guid}</c>, pelo
    /// mesmo motivo). O corpo já passou por
    /// <see cref="ValidationFilter{TRequest}"/> (<see cref="UpdateTaskHttpRequestValidator"/>)
    /// antes deste método rodar.
    /// </summary>
    private static async Task<IResult> HandleUpdateTaskAsync(
        string id, UpdateTaskHttpRequest request, ITasksBackend tasksBackend, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out _))
        {
            var errors = new Dictionary<string, string[]>
            {
                ["id"] = ["O id da tarefa deve ser um Guid válido."],
            };

            return Results.ValidationProblem(errors);
        }

        var response = await tasksBackend.UpdateTaskAsync(id, request, cancellationToken);

        return Results.Ok(response);
    }

    /// <summary>BE-20 — sem corpo de requisição; mesma checagem de formato de id de <see cref="HandleGetTaskAsync"/>.</summary>
    private static async Task<IResult> HandleCompleteTaskAsync(
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

        var response = await tasksBackend.CompleteTaskAsync(id, cancellationToken);

        return Results.Ok(response);
    }

    /// <summary>BE-20 — sem corpo de requisição; mesma checagem de formato de id de <see cref="HandleGetTaskAsync"/>.</summary>
    private static async Task<IResult> HandleReopenTaskAsync(
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

        var response = await tasksBackend.ReopenTaskAsync(id, cancellationToken);

        return Results.Ok(response);
    }

    /// <summary>
    /// BE-21 — soft delete no Tasks; sucesso devolve 204 sem corpo (mesma
    /// checagem de formato de id de <see cref="HandleGetTaskAsync"/>).
    /// </summary>
    private static async Task<IResult> HandleDeleteTaskAsync(
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

        await tasksBackend.DeleteTaskAsync(id, cancellationToken);

        return Results.NoContent();
    }
}
