using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TodoList.Tasks.IntegrationTests;

/// <summary>
/// BE-35, CA-11 — nenhum endpoint REST de tarefa está mapeado no Tasks
/// Service: enumera as rotas via <see cref="EndpointDataSource"/> do
/// <see cref="WebApplicationFactory{TEntryPoint}"/> e falha se qualquer coisa
/// além de <c>/health</c>, <c>/health/ready</c> e os RPCs gRPC esperados
/// (<c>tasks.v1.TasksService/CreateTask</c>,
/// <c>grpc.health.v1.Health/Check</c>, <c>grpc.health.v1.Health/Watch</c>)
/// aparecer.
/// </summary>
public class RouteInventoryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly HashSet<string> _rotasEsperadas =
    [
        "/health",
        "/health/",
        "/health/ready",
        "/tasks.v1.TasksService/CreateTask",
        "/grpc.health.v1.Health/Check",
        "/grpc.health.v1.Health/Watch",
        // Catch-all gerado pelo próprio Grpc.AspNetCore.Server para method
        // não implementado dentro de um serviço conhecido, ou serviço
        // desconhecido — devolve Unimplemented, nunca 404/rota de negócio.
        "{unimplementedService}/{unimplementedMethod:grpcunimplemented}",
        "tasks.v1.TasksService/{unimplementedMethod:grpcunimplemented}",
        "grpc.health.v1.Health/{unimplementedMethod:grpcunimplemented}",
    ];

    private readonly WebApplicationFactory<Program> _factory;

    public RouteInventoryTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact] // CA-11
    public void MapEndpoints_ApenasHealthEOsRpcsGrpcEsperados_EstaoMapeados()
    {
        // Força a construção do host — as rotas só existem depois disso.
        using var client = _factory.CreateClient();

        var endpointDataSource = _factory.Services.GetRequiredService<EndpointDataSource>();

        var rotasMapeadas = endpointDataSource.Endpoints
            .Select(endpoint => endpoint is RouteEndpoint routeEndpoint
                ? routeEndpoint.RoutePattern.RawText ?? endpoint.DisplayName
                : endpoint.DisplayName)
            .Where(rota => rota is not null)
            .Select(rota => rota!)
            .ToList();

        rotasMapeadas.Should().NotBeEmpty();
        rotasMapeadas.Should().BeSubsetOf(
            _rotasEsperadas,
            "nenhum endpoint REST de tarefa (removido por BE-35) pode voltar a ser mapeado — só health e os RPCs gRPC esperados");
    }
}
