using Microsoft.Extensions.Diagnostics.HealthChecks;
using TodoList.Tasks.Application.Identity;

namespace TodoList.Tasks.Infrastructure.Identity;

/// <summary>
/// Alcance do Identity por gRPC no <c>/health/ready</c> do Tasks (BE-24). Identity fora do ar é
/// <see cref="HealthStatus.Degraded"/>, não <c>Unhealthy</c>: o Tasks segue de pé e só deixa de criar tarefas
/// (fail-closed, D-28). Usa o cliente já registrado — timeout vem de <c>Identity:GrpcTimeoutSeconds</c>.
/// </summary>
public sealed class IdentityReachabilityHealthCheck : IHealthCheck
{
    /// <summary>Tag que tira este check do health gRPC (probe do Cloud Run, D-37): ele só vale para o readiness HTTP.</summary>
    public const string Tag = "identity-reachability";

    private readonly IIdentityGateway _identity;

    public IdentityReachabilityHealthCheck(IIdentityGateway identity)
    {
        _identity = identity;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Id que nunca existe: só interessa que a chamada gRPC complete (exists=false é resposta normal).
            await _identity.ValidateUserAsync(Guid.Empty, cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (IdentityUnavailableException)
        {
            return HealthCheckResult.Degraded("Identity inalcançável por gRPC.");
        }
    }
}
