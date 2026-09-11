namespace TodoList.Gateway.Api.Endpoints;

/// <summary>
/// Liveness simples do Gateway (BE-36, CA-14) — sempre anônimo, nunca
/// depende de Identity/Tasks estarem de pé: só confirma que o processo está
/// aceitando requisições.
/// </summary>
public sealed class HealthEndpoints : IEndpointRouteHandler
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => Results.Ok())
            .WithName("GetHealth")
            .WithSummary("Liveness check do API Gateway")
            .WithTags("Health")
            .AllowAnonymous();
    }
}
