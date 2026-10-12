using System.Globalization;
using FluentValidation;
using TodoList.Gateway.Api.Contracts;

namespace TodoList.Gateway.Api.Validation;

/// <summary>
/// Validador de <see cref="CreateTaskHttpRequest"/> (BE-36, CA-05/CA-06/CA-08)
/// — defesa em profundidade: duplica, de propósito, os mesmos limites que o
/// Tasks aplica (RN-TASK-02/03/04), para rejeitar na borda o que já dá para
/// rejeitar sem gastar uma chamada de rede. O Tasks continua sendo a
/// autoridade final — um chamador gRPC direto não pode confiar só nesta
/// cópia.
/// </summary>
public sealed class CreateTaskHttpRequestValidator : AbstractValidator<CreateTaskHttpRequest>
{
    /// <summary>Título: obrigatório, 1–200 caracteres após <c>Trim</c> (RN-TASK-02).</summary>
    public const int TitleMaxLength = 200;

    /// <summary>Descrição: até 2000 caracteres (RN-TASK-03).</summary>
    public const int DescriptionMaxLength = 2000;

    private static readonly string[] _validPriorities = ["Low", "Medium", "High"];

    public CreateTaskHttpRequestValidator()
    {
        RuleFor(request => request.Title)
            .Must(title => !string.IsNullOrWhiteSpace(title))
            .WithMessage("O título é obrigatório e não pode conter apenas espaços.");

        RuleFor(request => request.Title)
            .Must(title => title!.Trim().Length <= TitleMaxLength)
            .WithMessage($"O título deve ter no máximo {TitleMaxLength} caracteres.")
            .When(request => !string.IsNullOrWhiteSpace(request.Title));

        RuleFor(request => request.Description)
            .MaximumLength(DescriptionMaxLength)
            .WithMessage($"A descrição deve ter no máximo {DescriptionMaxLength} caracteres.")
            .When(request => request.Description is not null);

        RuleFor(request => request.Priority)
            .Must(priority => _validPriorities.Contains(priority, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"A prioridade deve ser uma das seguintes: {string.Join(", ", _validPriorities)}.")
            .When(request => request.Priority is not null);

        RuleFor(request => request.DueDate)
            .Must(BeAValidDate)
            .WithMessage("A data de vencimento deve estar no formato 'yyyy-MM-dd'.")
            .When(request => request.DueDate is not null);
    }

    private static bool BeAValidDate(string? dueDate) =>
        DateOnly.TryParseExact(dueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
