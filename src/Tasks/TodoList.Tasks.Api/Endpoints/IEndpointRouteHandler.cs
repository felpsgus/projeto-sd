namespace TodoList.Tasks.Api.Endpoints;

/// <summary>
/// Contrato implementado por cada grupo de endpoints (feature) da API.
/// Cada implementação usa <see cref="IEndpointRouteBuilder.MapGroup(string)"/>
/// para registrar suas rotas — nada de Controllers.
/// </summary>
public interface IEndpointRouteHandler
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints);
}
