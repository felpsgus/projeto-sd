using FluentValidation;
using TodoList.Gateway.Api.Contracts;

namespace TodoList.Gateway.Api.Validation;

/// <summary>
/// Validador de <see cref="DeleteAccountHttpRequest"/> (BE-16, D-19) — só
/// garante a presença da senha de confirmação; a checagem em si é regra de
/// negócio do Identity.
/// </summary>
public sealed class DeleteAccountHttpRequestValidator : AbstractValidator<DeleteAccountHttpRequest>
{
    public DeleteAccountHttpRequestValidator()
    {
        RuleFor(request => request.Password)
            .NotEmpty()
            .WithMessage("A senha é obrigatória.");
    }
}
