using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace TodoList.Tasks.Api.ErrorHandling;

/// <summary>
/// Fiação do tratamento de erros (BE-03): handler global de exceções
/// (<see cref="GlobalExceptionHandler"/>) + <c>ProblemDetails</c> com
/// <c>traceId</c> em todo corpo de erro (CA-03, CA-06) — validação (400),
/// erro de negócio via <see cref="ResultMapping.ResultHttpResults"/> e exceção não
/// tratada (500) passam todos pelo mesmo <c>IProblemDetailsService</c>, então
/// o <c>traceId</c> é adicionado num único lugar.
///
/// <para>
/// Usada tanto por <c>Program.cs</c> (produção) quanto por hosts de teste
/// mínimos criados nos projetos de teste — nenhuma rota de exemplo entra na
/// aplicação de produção só para exercitar este mecanismo.
/// </para>
/// </summary>
public static class ApiErrorHandlingExtensions
{
    public static IServiceCollection AddApiErrorHandling(this IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            };
        });

        return services;
    }

    public static IApplicationBuilder UseApiErrorHandling(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();

        return app;
    }
}
