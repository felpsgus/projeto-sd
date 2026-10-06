using System.Globalization;
using FluentValidation;
using TodoList.Gateway.Api.Contracts;

namespace TodoList.Gateway.Api.Validation;

/// <summary>
/// Validador de <see cref="UpdateTaskHttpRequest"/> (BE-19, CA-06/CA-15) —
/// espelha, campo a campo, <see cref="CreateTaskHttpRequestValidator"/>: os
/// dois DTOs compartilham a mesma forma e as mesmas regras (RN-TASK-02 a
/// RN-TASK-05), a mesma defesa em profundidade na borda antes de gastar uma
/// chamada gRPC. Não há como reutilizar a <b>mesma instância</b> de
/// <see cref="IValidator{T}"/> entre os dois (<see cref="Validation.ValidationFilter{TRequest}"/>
/// resolve por <c>IValidator&lt;TRequest&gt;</c>, um tipo por DTO) — o Tasks
/// Service resolve isso do lado dele reaproveitando a mesma instância sobre
/// um request efêmero (BE-19, CA-15); aqui, a defesa em profundidade da borda
/// aceita a duplicação de regra entre dois validators pequenos, cada um
/// tipado ao seu DTO.
/// </summary>
public sealed class UpdateTaskHttpRequestValidator : AbstractValidator<UpdateTaskHttpRequest>
{
    /// <summary>Título: obrigatório, 1–200 caracteres após <c>Trim</c> (RN-TASK-02) — mesmo limite de <see cref="CreateTaskHttpRequestValidator.TitleMaxLength"/>.</summary>
    public const int TitleMaxLength = CreateTaskHttpRequestValidator.TitleMaxLength;

    /// <summary>Descrição: até 2000 caracteres (RN-TASK-03) — mesmo limite de <see cref="CreateTaskHttpRequestValidator.DescriptionMaxLength"/>.</summary>
    public const int DescriptionMaxLength = CreateTaskHttpRequestValidator.DescriptionMaxLength;

    private static readonly string[] _validPriorities = ["Low", "Medium", "High"];

    public UpdateTaskHttpRequestValidator()
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
