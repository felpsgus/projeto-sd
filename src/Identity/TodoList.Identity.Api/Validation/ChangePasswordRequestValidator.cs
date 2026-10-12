using FluentValidation;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Security;

namespace TodoList.Identity.Api.Validation;

/// <summary>
/// Validador de borda da troca de senha (BE-15, CA-05): a nova senha pela
/// política de BE-06 (<see cref="PasswordPolicy"/>), reportando todas as
/// violações de uma vez. Não valida <see cref="ChangePasswordRequest.CurrentPassword"/>
/// aqui — conferir a senha atual é regra de negócio (exige consultar o
/// usuário), não formato de borda; fica em <see cref="ChangePasswordHandler"/>.
/// </summary>
public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(request => request.NewPassword)
            .Custom((newPassword, context) =>
            {
                foreach (var error in PasswordPolicy.Validate(newPassword))
                {
                    context.AddFailure(context.PropertyPath, error.Message);
                }
            });
    }
}
