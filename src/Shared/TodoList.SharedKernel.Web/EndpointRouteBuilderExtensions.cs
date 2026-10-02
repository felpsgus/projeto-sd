using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace TodoList.SharedKernel.Web;

/// <summary>
/// Descobre, por reflexão, todos os <see cref="IEndpointRouteHandler"/> definidos
/// nos assemblies informados e os registra. Novas features só precisam implementar
/// a interface — não é preciso lembrar de registrar manualmente em <c>Program.cs</c>.
/// O assembly de cada serviço é passado explicitamente porque este código vive
/// no SharedKernel.Web, não no assembly da API.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder endpoints, params Assembly[] assemblies)
    {
        var handlerType = typeof(IEndpointRouteHandler);

        var handlers = assemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => handlerType.IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false })
            .Select(type => (IEndpointRouteHandler)Activator.CreateInstance(type)!);

        foreach (var handler in handlers)
        {
            handler.MapEndpoints(endpoints);
        }

        return endpoints;
    }
}
