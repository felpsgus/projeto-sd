using FluentValidation;
using TodoList.Gateway.Api.Contracts;

namespace TodoList.Gateway.Api.Validation;

/// <summary>
/// Validador de <see cref="LoginHttpRequest"/> (BE-36) — só garante presença
/// de e-mail e senha; quem decide se a credencial é válida é sempre o
/// Identity (RN-AUTH-09), nunca este validador.
/// </summary>
public sealed class LoginHttpRequestValidator : AbstractValidator<LoginHttpRequest>
{
    public LoginHttpRequestValidator()
    {
        RuleFor(request => request.Email)
            .NotEmpty()
            .WithMessage("O e-mail é obrigatório.");

        RuleFor(request => request.Password)
            .NotEmpty()
            .WithMessage("A senha é obrigatória.");
    }
}
