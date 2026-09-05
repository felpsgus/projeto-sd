namespace TodoList.Tasks.Domain.Tasks;

/// <summary>
/// Prioridade de uma <see cref="TodoTask"/> (RN-TASK-04): <c>Low</c>, <c>Medium</c>
/// ou <c>High</c> — sem string mágica em lugar nenhum do código. O padrão,
/// quando não informado em <see cref="TodoTask.Create"/>, é <see cref="Medium"/>.
///
/// <para>
/// Valores numéricos fixados explicitamente: é o que a Infrastructure persiste
/// como <c>int</c> (BE-05). Deixar implícito (0, 1, 2 pela ordem de declaração)
/// funcionaria hoje, mas uma reordenação futura do enum corromperia dado já
/// gravado sem que o compilador acusasse nada — fixar o valor é o que torna
/// esse acidente impossível.
/// </para>
/// </summary>
public enum TaskPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
}
