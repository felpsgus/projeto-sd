using TodoList.SharedKernel;
using TodoList.Tasks.Domain.Common;

namespace TodoList.Tasks.Domain.Tasks;

/// <summary>
/// Tarefa de uma lista de afazeres (RN-TASK-01 a RN-TASK-09, RN-TASK-14,
/// RN-TASK-16, RN-AUTZ-01). Toda transição de estado e toda validação de
/// estrutura vivem aqui — nenhum caso de uso (BE-17 em diante) revalida
/// título, prioridade ou transição.
///
/// <para>
/// <b>CA-21 — sem setter público:</b> os campos de negócio só mudam por
/// <see cref="Create"/>/<see cref="UpdateDetails"/>/<see cref="Complete"/>/
/// <see cref="Reopen"/>/<see cref="SoftDelete"/>. <see cref="CreatedAt"/>,
/// <see cref="UpdatedAt"/> e <see cref="DeletedAt"/> (exigidos com setter
/// público pelas interfaces <see cref="IAuditable"/>/<see cref="ISoftDeletable"/>,
/// para que o <c>AuditingSaveChangesInterceptor</c> da Infrastructure consiga
/// escrevê-los via <c>entry.Entity.CreatedAt = ...</c>) são implementados
/// explicitamente: a leitura continua pública através da propriedade normal,
/// mas o setter só existe através da interface — inacessível como
/// <c>tarefa.CreatedAt = x</c> a partir de código comum, só via
/// <c>((IAuditable)tarefa).CreatedAt = x</c>.
/// </para>
///
/// <para>
/// <b>Por que os métodos de mutação recebem <see cref="TimeProvider"/> mesmo
/// quando a tabela de BE-05 não lista o parâmetro (caso de
/// <see cref="UpdateDetails"/> e <see cref="Reopen"/>):</b> CA-14 exige um
/// teste de unidade por método (<c>UpdateDetails</c>, <c>Complete</c>,
/// <c>Reopen</c>, <c>SoftDelete</c>) provando que <see cref="UpdatedAt"/>
/// reflete um <c>TimeProvider</c> avançado — sem gravar em banco, já que é
/// teste de Domain puro. Isso só é possível se o próprio método souber "que
/// horas são". A alternativa seria a entidade guardar uma referência a
/// <see cref="TimeProvider"/> desde a criação, mas isso a transformaria numa
/// entidade com dependência de infraestrutura embutida (e inutilizável assim
/// que reidratada do banco, onde não há <c>TimeProvider</c> nenhum para
/// injetar de volta). Receber o parâmetro em cada chamada mantém o Domain
/// puro (nada de <c>DateTime.UtcNow</c> direto, mesma convenção de BE-02) e
/// resolve a exigência de CA-14 sem estado extra. Em produção, o
/// <c>AuditingSaveChangesInterceptor</c> volta a escrever <c>UpdatedAt</c> no
/// <c>SaveChangesAsync</c> com o mesmo <see cref="TimeProvider"/> injetado por
/// DI — redundante com o que o método de domínio já fez, mas inofensivo: é o
/// mesmo relógio, então o mesmo instante.
/// </para>
/// </summary>
public sealed class TodoTask : IAuditable, ISoftDeletable
{
    /// <summary>RN-TASK-02: 1 a 200 caracteres.</summary>
    public const int TitleMaxLength = 200;

    /// <summary>RN-TASK-03: no máximo 2000 caracteres quando informada.</summary>
    public const int DescriptionMaxLength = 2000;

    // Só para materialização pelo EF Core (Infrastructure) — nunca chamado
    // pelo código de negócio, que sempre passa por Create(). Não precisa de
    // pragma para CS8618: o inicializador de Title (abaixo, "= string.Empty")
    // já satisfaz a análise de nulidade sem suprimir warning nenhum — o EF
    // sobrescreve o valor via o setter privado ao reidratar a linha.
    private TodoTask()
    {
    }

    private TodoTask(Guid id, Guid ownerId, string title, string? description, TaskPriority priority, DateOnly? dueDate, DateTime now)
    {
        Id = id;
        OwnerId = ownerId;
        Title = title;
        Description = description;
        Priority = priority;
        DueDate = dueDate;
        Status = TodoTaskStatus.Pending;
        CompletedAt = null;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Dono da tarefa (RN-AUTZ-01), obrigatório e <b>imutável</b> — nenhum
    /// método desta classe altera <see cref="OwnerId"/> depois de
    /// <see cref="Create"/>.
    ///
    /// <para>
    /// <b>Não é navegação para <c>User</c>, de propósito.</b> A tabela de
    /// BE-05 descreve <c>OwnerId</c> como "FK para <c>User</c>", mas isso
    /// colide com D-27/CA-13 de BE-02: <c>TasksDbContext</c> não pode mapear
    /// nada do schema <c>identity</c> — é a barreira, reforçada por teste, que
    /// impede trocar a chamada gRPC (<c>IIdentityGateway</c>) por um
    /// <c>JOIN</c> entre serviços. <c>OwnerId</c> é só <c>Guid</c>: sem
    /// propriedade de navegação, sem <c>HasOne</c>/<c>WithMany</c> no
    /// mapeamento. A FK real cruzando schemas (<c>tasks.tasks.owner_id →
    /// identity.users(id) ON DELETE CASCADE</c>) é responsabilidade de BE-02
    /// CA-02c, declarada por SQL explícito numa migration própria — fora do
    /// escopo desta task.
    /// </para>
    /// </summary>
    public Guid OwnerId { get; private set; }

    /// <summary>Sempre armazenado com <c>Trim()</c> (CA-02) — 1 a 200 caracteres (RN-TASK-02).</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Opcional; no máximo 2000 caracteres quando informada (RN-TASK-03).</summary>
    public string? Description { get; private set; }

    public TodoTaskStatus Status { get; private set; }

    public TaskPriority Priority { get; private set; }

    /// <summary>
    /// Opcional; aceita data passada (RN-TASK-05/D-06) — quem sinaliza
    /// atraso é <see cref="IsOverdue"/>, nunca a criação/edição.
    /// </summary>
    public DateOnly? DueDate { get; private set; }

    /// <summary>Preenchida só quando <see cref="Status"/> é <see cref="TodoTaskStatus.Completed"/>.</summary>
    public DateTime? CompletedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public DateTime? DeletedAt { get; private set; }

    // Explicit interface implementation (ver comentário de classe, CA-21):
    // o setter só é alcançável através da interface, nunca como membro
    // público comum de TodoTask.
    DateTime IAuditable.CreatedAt
    {
        get => CreatedAt;
        set => CreatedAt = value;
    }

    DateTime IAuditable.UpdatedAt
    {
        get => UpdatedAt;
        set => UpdatedAt = value;
    }

    DateTime? ISoftDeletable.DeletedAt
    {
        get => DeletedAt;
        set => DeletedAt = value;
    }

    /// <summary>
    /// Cria uma tarefa nova: sempre <see cref="TodoTaskStatus.Pending"/>
    /// (RN-TASK-07), <see cref="CompletedAt"/> nulo, prioridade
    /// <see cref="TaskPriority.Medium"/> quando <paramref name="priority"/>
    /// não for informada (RN-TASK-04).
    /// </summary>
    /// <param name="ownerId">Dono da tarefa; obrigatório (CA-08).</param>
    /// <param name="title">Título; 1–200 caracteres após <c>Trim()</c>, não só espaços (CA-01, CA-02).</param>
    /// <param name="description">Descrição opcional; no máximo 2000 caracteres (CA-03).</param>
    /// <param name="priority">Prioridade; <see cref="TaskPriority.Medium"/> quando omitida (CA-04). Um valor de enum fora do intervalo declarado é rejeitado (CA-05).</param>
    /// <param name="dueDate">Vencimento opcional; datas passadas são aceitas (CA-06).</param>
    /// <param name="timeProvider">Relógio injetado — nunca <see cref="DateTime.UtcNow"/> direto.</param>
    public static Result<TodoTask> Create(
        Guid ownerId,
        string title,
        string? description,
        TaskPriority? priority,
        DateOnly? dueDate,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (ownerId == Guid.Empty)
        {
            return Result.Failure<TodoTask>(TodoTaskErrors.OwnerRequired);
        }

        var titleValidation = ValidateTitle(title);
        if (titleValidation.IsFailure)
        {
            return Result.Failure<TodoTask>(titleValidation.Error);
        }

        var descriptionValidation = ValidateDescription(description);
        if (descriptionValidation.IsFailure)
        {
            return Result.Failure<TodoTask>(descriptionValidation.Error);
        }

        var resolvedPriority = priority ?? TaskPriority.Medium;
        if (!Enum.IsDefined(resolvedPriority))
        {
            return Result.Failure<TodoTask>(TodoTaskErrors.InvalidPriority);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        return new TodoTask(Guid.NewGuid(), ownerId, titleValidation.Value, description, resolvedPriority, dueDate, now);
    }

    /// <summary>Edita título, descrição, prioridade e vencimento (RN-TASK-11). Não altera <see cref="OwnerId"/> nem <see cref="Status"/>.</summary>
    public Result UpdateDetails(string title, string? description, TaskPriority priority, DateOnly? dueDate, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var titleValidation = ValidateTitle(title);
        if (titleValidation.IsFailure)
        {
            return Result.Failure(titleValidation.Error);
        }

        var descriptionValidation = ValidateDescription(description);
        if (descriptionValidation.IsFailure)
        {
            return Result.Failure(descriptionValidation.Error);
        }

        if (!Enum.IsDefined(priority))
        {
            return Result.Failure(TodoTaskErrors.InvalidPriority);
        }

        Title = titleValidation.Value;
        Description = description;
        Priority = priority;
        DueDate = dueDate;
        UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        return Result.Success();
    }

    /// <summary>RN-TASK-08: só a partir de <see cref="TodoTaskStatus.Pending"/>. Grava <see cref="CompletedAt"/>.</summary>
    public Result Complete(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (Status == TodoTaskStatus.Completed)
        {
            return Result.Failure(TodoTaskErrors.AlreadyCompleted);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        Status = TodoTaskStatus.Completed;
        CompletedAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>RN-TASK-09: só a partir de <see cref="TodoTaskStatus.Completed"/>. Limpa <see cref="CompletedAt"/>.</summary>
    public Result Reopen(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (Status != TodoTaskStatus.Completed)
        {
            return Result.Failure(TodoTaskErrors.NotCompleted);
        }

        Status = TodoTaskStatus.Pending;
        CompletedAt = null;
        UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        return Result.Success();
    }

    /// <summary>RN-TASK-13 (D-07): remoção lógica — marca <see cref="DeletedAt"/>. Falha (idempotência) se já removida.</summary>
    public Result SoftDelete(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (DeletedAt is not null)
        {
            return Result.Failure(TodoTaskErrors.AlreadyDeleted);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        DeletedAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// RN-TASK-16: condição <b>derivada</b>, nunca coluna persistida (CA-17)
    /// — <c>Pending</c> e <see cref="DueDate"/> anterior a <paramref name="today"/>.
    /// Recebe "hoje" por parâmetro (D-18): o Domain não conhece fuso nem
    /// relógio; quem resolve "hoje" é a borda (<c>IClientDate</c>, BE-13),
    /// não este método.
    /// </summary>
    public bool IsOverdue(DateOnly today) =>
        Status == TodoTaskStatus.Pending && DueDate is not null && DueDate < today;

    private static Result<string> ValidateTitle(string title)
    {
        var trimmed = title?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return Result.Failure<string>(TodoTaskErrors.TitleRequired);
        }

        if (trimmed.Length > TitleMaxLength)
        {
            return Result.Failure<string>(TodoTaskErrors.TitleTooLong);
        }

        return trimmed;
    }

    private static Result ValidateDescription(string? description)
    {
        if (description is not null && description.Length > DescriptionMaxLength)
        {
            return Result.Failure(TodoTaskErrors.DescriptionTooLong);
        }

        return Result.Success();
    }
}
