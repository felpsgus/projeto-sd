using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace TodoList.Tasks.Api.Validation;

/// <summary>
/// Filtro de endpoint (BE-03) que roda o <see cref="IValidator{T}"/> de
/// <typeparamref name="TRequest"/> registrado via DI **antes** do handler.
/// Falha de validação vira 400 com um <c>ProblemDetails</c> contendo os erros
/// por campo (CA-04) — nunca uma mensagem única concatenada.
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
            return Microsoft.AspNetCore.Http.Results.ValidationProblem(validationResult.ToDictionary());
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
