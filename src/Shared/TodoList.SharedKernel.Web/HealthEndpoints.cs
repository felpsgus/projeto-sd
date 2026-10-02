using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace TodoList.SharedKernel.Web;

/// <summary>
/// Health checks (BE-02, CA-03/CA-04), via <c>MapHealthChecks</c> — Minimal
/// APIs, nunca Controller.
/// </summary>
public sealed class HealthEndpoints : IEndpointRouteHandler
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/health").WithTags("Health");

        // Liveness: nunca executa nenhum health check registrado (Predicate
        // sempre falso) — não depende de banco, cache ou serviço externo, só
        // confirma que o processo está de pé e aceitando requisições.
        group.MapHealthChecks(string.Empty, new HealthCheckOptions { Predicate = _ => false })
            .WithName("GetHealthLive")
            .WithSummary("Liveness check do serviço")
            .AllowAnonymous();

        // Readiness: roda só os checks marcados com a tag "ready" — hoje, a
        // conectividade com o Postgres (AddXxxDatabaseHealthCheck).
        // Banco fora do ar não derruba o processo (CA-03): este endpoint
        // responde 503 (degradado), que é o comportamento padrão do
        // MapHealthChecks para status != Healthy.
        group.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
            .WithName("GetHealthReady")
            .WithSummary("Readiness check do serviço (conectividade com o banco)")
            .AllowAnonymous();
    }
}
