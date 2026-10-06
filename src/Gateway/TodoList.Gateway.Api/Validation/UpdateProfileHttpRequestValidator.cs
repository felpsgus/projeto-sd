using FluentValidation;
using TodoList.Gateway.Api.Contracts;

namespace TodoList.Gateway.Api.Validation;

/// <summary>
/// Validador de <see cref="UpdateProfileHttpRequest"/> (BE-14, CA-06/CA-07) —
/// <c>displayName</c> obrigatório, 1–100 caracteres após <c>Trim</c>, não pode
/// ser só espaços.
/// </summary>
public sealed class UpdateProfileHttpRequestValidator : AbstractValidator<UpdateProfileHttpRequest>
{
    /// <summary>Tamanho máximo do nome de exibição após trim (BE-14, CA-06/CA-07).</summary>
    public const int DisplayNameMaxLength = 100;

    public UpdateProfileHttpRequestValidator()
    {
        RuleFor(request => request.DisplayName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("O nome de exibição é obrigatório e não pode conter apenas espaços.");

        RuleFor(request => request.DisplayName)
            .Must(name => name!.Trim().Length <= DisplayNameMaxLength)
            .WithMessage($"O nome de exibição deve ter no máximo {DisplayNameMaxLength} caracteres.")
            .When(request => !string.IsNullOrWhiteSpace(request.DisplayName));
    }
}
