namespace TodoList.Gateway.Api.Endpoints;

/// <summary>
/// Contrato implementado por cada grupo de endpoints (feature) do Gateway.
/// Vive aqui, e não no SharedKernel.Web, porque o Gateway não referencia os
/// projetos compartilhados (D-33). Identity e Tasks tinham a mesma interface
/// até serem reduzidos a um único endpoint HTTP (<c>/health</c>): lá a varredura
/// por reflexão descobria uma classe só, e saiu.
/// Cada implementação usa <see cref="IEndpointRouteBuilder.MapGroup(string)"/>
/// para registrar suas rotas — nada de Controllers.
/// </summary>
public interface IEndpointRouteHandler
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints);
}
