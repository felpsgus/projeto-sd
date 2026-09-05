using TodoList.SharedKernel;

namespace TodoList.Tasks.Domain.Tasks;

/// <summary>
/// Catálogo de erros de negócio da entidade <see cref="TodoTask"/> (BE-05,
/// seguindo o mesmo padrão de catálogo estático por serviço já usado em
/// <c>TodoList.Tasks.Application.Errors.TaskErrors</c> — D-26: só o tipo
/// <see cref="Error"/> é compartilhado, o catálogo não). Fica em
/// <c>Domain</c>, não em <c>Application</c>: são erros de invariante da
/// própria entidade (título, descrição, prioridade, transição de estado),
/// levantados pelos métodos de <see cref="TodoTask"/> — não por um caso de
/// uso (esses só chegam em BE-17 a BE-22).
/// </summary>
public static class TodoTaskErrors
{
    public static readonly Error OwnerRequired = new(
        "task.owner_required",
        "A tarefa precisa de um dono.",
        ErrorType.Validation);

    public static readonly Error TitleRequired = new(
        "task.title_required",
        "O título é obrigatório e não pode conter apenas espaços.",
        ErrorType.Validation);

    public static readonly Error TitleTooLong = new(
        "task.title_too_long",
        $"O título deve ter no máximo {TodoTask.TitleMaxLength} caracteres.",
        ErrorType.Validation);

    public static readonly Error DescriptionTooLong = new(
        "task.description_too_long",
        $"A descrição deve ter no máximo {TodoTask.DescriptionMaxLength} caracteres.",
        ErrorType.Validation);

    public static readonly Error InvalidPriority = new(
        "task.invalid_priority",
        "Prioridade inválida.",
        ErrorType.Validation);

    /// <summary>RN-TASK-08: só uma tarefa <c>Pending</c> pode ser concluída.</summary>
    public static readonly Error AlreadyCompleted = new(
        "task.already_completed",
        "A tarefa já está concluída.",
        ErrorType.Conflict);

    /// <summary>RN-TASK-09: só uma tarefa <c>Completed</c> pode ser reaberta.</summary>
    public static readonly Error NotCompleted = new(
        "task.not_completed",
        "Só é possível reabrir uma tarefa concluída.",
        ErrorType.Conflict);

    /// <summary>Guarda de idempotência: remover uma tarefa já removida é conflito, não sucesso silencioso.</summary>
    public static readonly Error AlreadyDeleted = new(
        "task.already_deleted",
        "A tarefa já foi removida.",
        ErrorType.Conflict);
}
