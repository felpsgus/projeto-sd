using FluentValidation;
using TodoList.Gateway.Api.Contracts;

namespace TodoList.Gateway.Api.Validation;

/// <summary>
/// Validador de <see cref="RegisterHttpRequest"/> (BE-07, CA-07/CA-08) —
/// defesa em profundidade: e-mail com formato válido (RN-AUTH-03) e senha
/// pela política de RN-AUTH-04, reportando todas as violações de uma vez, na
/// borda, antes de qualquer chamada gRPC. O Identity continua sendo a
/// autoridade final — o Gateway não referencia o projeto do Identity (D-33),
/// então a política de senha é duplicada aqui, não compartilhada (mesma
/// decisão de <see cref="CreateTaskHttpRequestValidator"/> para os limites do
/// Tasks). <see cref="RegisterHttpRequest.DisplayName"/> é opcional — nenhuma
/// regra aqui, o Identity trata ausente/só-espaços (RN-AUTH-07).
/// </summary>
public sealed class RegisterHttpRequestValidator : AbstractValidator<RegisterHttpRequest>
{
    /// <summary>Tamanho mínimo de senha (RN-AUTH-04) — mesmo valor de <c>PasswordPolicy.MinLength</c> no Identity.</summary>
    public const int PasswordMinLength = 8;

    public RegisterHttpRequestValidator()
    {
        RuleFor(request => request.Email)
            .Must(email => !string.IsNullOrWhiteSpace(email))
            .WithMessage("O e-mail é obrigatório.");

        RuleFor(request => request.Email)
            .EmailAddress()
            .WithMessage("O e-mail informado não tem um formato válido.")
            .When(request => !string.IsNullOrWhiteSpace(request.Email));

        RuleFor(request => request.Password)
            .Must(password => !string.IsNullOrEmpty(password) && password.Length >= PasswordMinLength)
            .WithMessage($"A senha deve ter pelo menos {PasswordMinLength} caracteres.");

        RuleFor(request => request.Password)
            .Must(password => password is not null && password.Any(char.IsLetter))
            .WithMessage("A senha deve conter ao menos uma letra.");

        RuleFor(request => request.Password)
            .Must(password => password is not null && password.Any(char.IsDigit))
            .WithMessage("A senha deve conter ao menos um número.");
    }
}
