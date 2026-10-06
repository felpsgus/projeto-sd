using FluentValidation;
using TodoList.Gateway.Api.Contracts;

namespace TodoList.Gateway.Api.Validation;

/// <summary>
/// Validador de <see cref="ChangePasswordHttpRequest"/> (BE-15) — só garante
/// presença dos dois campos; a política de força da nova senha (RN-AUTH-04) e
/// a checagem da senha atual são regra de negócio do Identity, não formato de
/// borda.
/// </summary>
public sealed class ChangePasswordHttpRequestValidator : AbstractValidator<ChangePasswordHttpRequest>
{
    public ChangePasswordHttpRequestValidator()
    {
        RuleFor(request => request.CurrentPassword)
            .NotEmpty()
            .WithMessage("A senha atual é obrigatória.");

        RuleFor(request => request.NewPassword)
            .NotEmpty()
            .WithMessage("A nova senha é obrigatória.");
    }
}
