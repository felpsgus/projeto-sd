namespace TodoList.Tasks.Api.Endpoints;

/// <summary>
/// Descobre, por reflexão, todos os <see cref="IEndpointRouteHandler"/> definidos
/// no assembly da API e os registra. Novas features só precisam implementar a
/// interface — não é preciso lembrar de registrar manualmente em <c>Program.cs</c>.
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
