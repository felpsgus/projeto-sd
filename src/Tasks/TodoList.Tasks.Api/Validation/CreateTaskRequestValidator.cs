using FluentValidation;
using TodoList.Tasks.Application.Tasks;
using TodoList.Tasks.Domain.Tasks;

namespace TodoList.Tasks.Api.Validation;

/// <summary>
/// Validador de <see cref="CreateTaskRequest"/> (BE-17) — espelha as regras
/// do domínio (RN-TASK-02, RN-TASK-03) para dar a mensagem por campo (400,
/// BE-03 CA-04) antes de qualquer chamada ao Identity (BE-28, CA-12). É
/// intencionalmente redundante com <c>TodoTask.Create</c>: o domínio segue
/// sendo a fonte de verdade da invariante, este validador só antecipa o erro
/// para quem chama o handler pela borda HTTP.
///
/// <para>
/// <see cref="CreateTaskRequest.Priority"/> e <see cref="CreateTaskRequest.DueDate"/>
/// não precisam de regra aqui: um valor de enum ou de data fora do formato
/// esperado já falha na desserialização do corpo JSON (400, antes deste
/// validador rodar) — ver <c>Program.cs</c> (<c>JsonStringEnumConverter</c>)
/// e o teste de integração correspondente.
/// </para>
/// </summary>
public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(request => request.Title)
            .NotEmpty()
            .WithMessage("O título é obrigatório e não pode conter apenas espaços.")
            .MaximumLength(TodoTask.TitleMaxLength)
            .WithMessage($"O título deve ter no máximo {TodoTask.TitleMaxLength} caracteres.");

        RuleFor(request => request.Description)
            .MaximumLength(TodoTask.DescriptionMaxLength)
            .WithMessage($"A descrição deve ter no máximo {TodoTask.DescriptionMaxLength} caracteres.")
            .When(request => request.Description is not null);
    }
}
