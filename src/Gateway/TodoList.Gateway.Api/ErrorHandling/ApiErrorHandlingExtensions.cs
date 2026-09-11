namespace TodoList.Gateway.Api.ErrorHandling;

/// <summary>
/// Fiação do tratamento de erros do Gateway (BE-36) — handler global
/// (<see cref="GlobalExceptionHandler"/>) + <c>ProblemDetails</c> com
/// <c>traceId</c> em todo corpo de erro, mesmo padrão de
/// <c>TodoList.Tasks.Api.ErrorHandling.ApiErrorHandlingExtensions</c>.
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
