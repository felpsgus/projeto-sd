namespace TodoList.Tasks.Domain.Tasks;

/// <summary>
/// Estado do ciclo de vida de uma <see cref="TodoTask"/> (RN-TASK-06):
/// <c>Pending</c> ou <c>Completed</c> — nenhum terceiro valor existe.
///
/// <para>
/// <b>Por que não se chama <c>TaskStatus</c>, como a tabela de BE-05 sugere:</b>
/// o BCL já declara <c>System.Threading.Tasks.TaskStatus</c>, e todo projeto
/// aqui compila com <c>ImplicitUsings</c> habilitado — inclusive
/// <c>global using System.Threading.Tasks;</c> (ver
/// <c>TodoList.Tasks.Domain.GlobalUsings.g.cs</c>). Um enum de domínio chamado
/// <c>TaskStatus</c> neste mesmo namespace não colidiria dentro do próprio
/// arquivo (um tipo do namespace corrente ganha do <c>using</c> global), mas
/// colidiria — <c>CS0104</c>, referência ambígua — em todo lugar que faz
/// <c>using TodoList.Tasks.Domain.Tasks;</c> e ainda enxerga o
/// <c>global using System.Threading.Tasks;</c> implícito: Application,
/// Infrastructure, Api e os projetos de teste, ou seja, praticamente todo
/// código de chamada.
/// </para>
///
/// <para>
/// Das três saídas sugeridas pela tarefa (alias, nome diferente, qualificação
/// explícita), nome diferente é a que deixa o código de chamada mais legível:
/// um alias (<c>using TaskStatus = TodoList.Tasks.Domain.Tasks.TodoTaskStatus;</c>)
/// precisaria ser repetido em cada arquivo consumidor, e qualificar sempre por
/// extenso (<c>TodoList.Tasks.Domain.Tasks.TaskStatus</c>) é ruído em toda
/// chamada. <c>TodoTaskStatus</c> — o mesmo prefixo já usado no nome da
/// entidade — não ambiguiza com nada do BCL e não exige nenhum using especial
/// de quem chama.
/// </para>
/// </summary>
public enum TodoTaskStatus
{
    Pending = 0,
    Completed = 1,
}
