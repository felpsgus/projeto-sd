using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TodoList.Tasks.Api.Configuration;
using TodoList.Tasks.Api.Endpoints;
using TodoList.Tasks.Application.Tasks;
using Xunit;

namespace TodoList.Tasks.UnitTests.Endpoints;

/// <summary>
/// BE-29, CA-12: <c>POST /api/tasks</c> só entra na allowlist de
/// <c>AllowAnonymous</c> quando <c>Tasks:AllowAnonymousCreate=true</c> —
/// verificado direto na metadata do endpoint mapeado (sem precisar do teste
/// de guarda de rotas completo de BE-13, que exigiria a fallback policy
/// global que ainda não existe no Tasks Service).
/// </summary>
public class TaskEndpointsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MapEndpoints_PostApiTasks_SoEntraNaAllowlistDeAllowAnonymous_QuandoOModoEstaLigado(bool allowAnonymousCreate)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.Configure<TasksCreationOptions>(options => options.AllowAnonymousCreate = allowAnonymousCreate);

        // Só para que o RequestDelegateFactory consiga inferir "handler" como
        // parâmetro de serviço ao montar a metadata do endpoint — nunca
        // resolvido de verdade neste teste (que não invoca a rota, só
        // inspeciona a metadata mapeada).
        builder.Services.AddScoped(_ => default(CreateTaskHandler)!);

        var app = builder.Build();

        new TaskEndpoints().MapEndpoints(app);

        // WebApplication implementa IEndpointRouteBuilder explicitamente —
        // DataSources só é alcançável através da interface.
        IEndpointRouteBuilder endpointRouteBuilder = app;
        var endpoint = endpointRouteBuilder.DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText != null && candidate.RoutePattern.RawText.Contains("api/tasks", StringComparison.Ordinal));

        var temAllowAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;

        temAllowAnonymous.Should().Be(allowAnonymousCreate);
    }
}
