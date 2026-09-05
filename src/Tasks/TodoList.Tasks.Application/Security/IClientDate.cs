namespace TodoList.Tasks.Application.Security;

/// <summary>
/// Data local do usuário corrente (decisão **D-18**, BE-13) — usada para
/// calcular <c>isOverdue</c> (RN-TASK-16) sem que o <c>Domain</c> precise
/// conhecer fuso horário ou relógio (ver <c>TodoTask.IsOverdue(DateOnly)</c>,
/// que recebe "hoje" por parâmetro).
///
/// <para>
/// Único ponto de leitura: nenhum caso de uso recebe <c>today</c> como
/// parâmetro próprio — todos injetam <see cref="IClientDate"/> e leem
/// <see cref="Today"/> (mesma regra que vale para <see cref="ICurrentUser"/>).
/// </para>
/// </summary>
public interface IClientDate
{
    /// <summary>
    /// A data local do usuário. Implementação lê o header
    /// <c>X-Client-Date: yyyy-MM-dd</c>; ausente/malformado cai em fallback
    /// silencioso para a data UTC do <c>TimeProvider</c> — nunca erro, nunca
    /// 400 (BE-13, nota técnica).
    /// </summary>
    public DateOnly Today { get; }
}
