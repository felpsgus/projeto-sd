namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Configuração tipada consumida por <see cref="CreateTaskHandler"/> (BE-17,
/// decisão D-08) — vinculada com <c>IOptions&lt;T&gt;</c> a partir da seção
/// <c>Tasks</c> de configuração, na <c>Api</c>. Não usa
/// <c>ValidateDataAnnotations</c>: <see cref="MaxActivePerUser"/> nulo é um
/// valor válido (desativa o limite), não um erro de configuração.
///
/// <para>
/// Mora em <c>Application</c>, e não em <c>Api</c> (ao contrário de
/// <c>ServiceOptions</c>): é o próprio caso de uso que lê o valor, não a
/// borda HTTP — colocá-la na <c>Api</c> obrigaria a <c>Application</c> a
/// depender da camada de fora, invertendo a regra de dependência (seção 2.1
/// das convenções).
/// </para>
/// </summary>
public sealed class TaskOptions
{
    /// <summary>
    /// Nome da seção de configuração. Compartilhada com
    /// <c>TodoList.Tasks.Api.Configuration.TasksCreationOptions</c> (BE-29,
    /// <c>Tasks:AllowAnonymousCreate</c>) — duas classes de opções distintas
    /// podem se vincular à mesma seção, cada uma lendo só a sua propriedade.
    /// </summary>
    public const string SectionName = "Tasks";

    /// <summary>
    /// Limite de tarefas ativas por usuário (RN-TASK-15). <c>500</c> por
    /// padrão (D-08); <c>null</c> desativa o limite.
    /// </summary>
    // "set" (não "init"): permite CreateTaskActiveLimitTests forçar null via
    // PostConfigure, sem precisar de uma segunda representação textual
    // ambígua de "sem valor" numa fonte de configuração in-memory.
    public int? MaxActivePerUser { get; set; } = 500;
}
