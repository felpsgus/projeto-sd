using FluentValidation;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Security;
using TodoList.Identity.Domain.Users;

namespace TodoList.Identity.Api.Validation;

/// <summary>
/// Validador de borda do cadastro (BE-07, CA-07/CA-08) — mesmo desenho de
/// <c>CreateTaskRequestValidator</c> (Tasks Service): FluentValidation vive na
/// Api (a borda), não na Application. E-mail com formato válido delega a
/// <see cref="Email.Create"/> (RN-AUTH-03, sem duplicar o regex); senha pela
/// política de BE-06 (<see cref="PasswordPolicy"/>), reportando <b>todas</b>
/// as violações de uma vez, não só a primeira (RN-AUTH-04).
/// </summary>
public sealed class RegisterUserRequestValidator : AbstractValidator<RegisterUserRequest>
{
    public RegisterUserRequestValidator()
    {
        RuleFor(request => request.Email)
            .Must(email => Email.Create(email).IsSuccess)
            .WithMessage("O e-mail informado não tem um formato válido.");

        RuleFor(request => request.Password)
            .Custom((password, context) =>
            {
                foreach (var error in PasswordPolicy.Validate(password))
                {
                    context.AddFailure(context.PropertyPath, error.Message);
                }
            });
    }
}
