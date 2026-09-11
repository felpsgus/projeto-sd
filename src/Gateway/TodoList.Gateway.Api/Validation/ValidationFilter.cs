using FluentValidation;

namespace TodoList.Gateway.Api.Validation;

/// <summary>
/// Filtro de endpoint (BE-36) que roda o <see cref="IValidator{T}"/> de
/// <typeparamref name="TRequest"/> registrado via DI **antes** do handler —
/// mesmo padrão de <c>TodoList.Tasks.Api.Validation.ValidationFilter</c>.
/// Falha de validação vira 400 com um <c>ValidationProblem</c> por campo
/// (CA-05/CA-06/CA-08), e nenhuma chamada gRPC ao backend acontece antes
/// dele rodar.
/// </summary>
public sealed class ValidationFilter<TRequest> : IEndpointFilter
    where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();

        if (request is null)
        {
            return await next(context);
        }

        var validator = context.HttpContext.RequestServices.GetService(typeof(IValidator<TRequest>)) as IValidator<TRequest>;

        if (validator is null)
        {
            return await next(context);
        }

        var validationResult = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);

        if (!validationResult.IsValid)
        {
            return Results.ValidationProblem(validationResult.ToDictionary());
        }

        return await next(context);
    }
}

/// <summary>Registra <see cref="ValidationFilter{TRequest}"/> num endpoint.</summary>
public static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder WithRequestValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : class =>
        builder.AddEndpointFilter<ValidationFilter<TRequest>>();
}
