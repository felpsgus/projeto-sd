namespace TodoList.Tasks.Api.Configuration;

/// <summary>
/// Configuração tipada do modo provisório de criação de tarefa (BE-29,
/// D-30). Vinculada à mesma seção <c>Tasks</c> de
/// <c>TodoList.Tasks.Application.Tasks.TaskOptions</c> — cada classe lê só a
/// sua propriedade; nada impede duas classes de configuração distintas
/// vinculadas à mesma seção.
///
/// <para>
/// Mora na <c>Api</c>, e não na <c>Application</c>: só a fiação HTTP
/// (seleção de <c>ICurrentUser</c>, filtro de endpoint, allowlist de rota)
/// decide com base nisto — o caso de uso (<c>CreateTaskHandler</c>) nunca lê
/// esta flag, nem sabe que ela existe (é exatamente o que garante que "a
/// Application não muda entre os dois modos").
/// </para>
/// </summary>
public sealed class TasksCreationOptions
{
    public const string SectionName = "Tasks";

    /// <summary>
    /// Quando <c>true</c>, <c>POST /api/tasks</c> aceita chamadas sem token,
    /// com o dono lido do header <c>X-User-Id</c> (D-30). <b>Padrão:
    /// <c>false</c></b> — risco assumido e delimitado (nota técnica de
    /// BE-29): inaceitável em qualquer ambiente exposto.
    /// </summary>
    // "set" (não "init"): IOptions<T> é vinculada por Bind() em Program.cs,
    // mas testes de integração precisam sobrescrever o valor depois de
    // construído (PostConfigure) — ver TaskEndpointsTests e
    // CreateTaskActiveLimitTests.
    public bool AllowAnonymousCreate { get; set; }
}
