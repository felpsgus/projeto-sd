namespace TodoList.Gateway.Api.Endpoints;

/// <summary>
/// Descobre, por reflexão, todos os <see cref="IEndpointRouteHandler"/> deste
/// assembly e os registra — mesmo padrão de
/// <c>TodoList.Tasks.Api.Endpoints.EndpointRouteBuilderExtensions</c>. Nova
/// feature só precisa implementar a interface.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var handlerType = typeof(IEndpointRouteHandler);

        var handlers = handlerType.Assembly
            .GetTypes()
            .Where(type => handlerType.IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false })
            .Select(type => (IEndpointRouteHandler)Activator.CreateInstance(type)!);

        foreach (var handler in handlers)
        {
            handler.MapEndpoints(endpoints);
        }

        return endpoints;
    }
}
