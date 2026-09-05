namespace TodoList.Tasks.Application.Security;

/// <summary>
/// Identidade de quem está chamando o caso de uso corrente — abstração de
/// <c>Application</c> (BE-13), com implementações diferentes em cada serviço
/// e, no Tasks, diferentes conforme o modo provisório de BE-29:
/// <list type="bullet">
/// <item>hoje, sempre lida do chamador — o header <c>X-User-Id</c> do gatilho
/// provisório ([BE-29], D-30), enquanto <c>Tasks:AllowAnonymousCreate=true</c>;</item>
/// <item>depois, do claim <c>sub</c> do token repassado pelo API Gateway
/// (D-32), quando BE-13 estiver pronto no Tasks.</item>
/// </list>
/// Nenhum caso de uso (<see cref="Tasks.CreateTaskHandler"/> incluído) sabe
/// qual das duas está em uso — só consome <see cref="Id"/>.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// O <see cref="Guid"/> do usuário corrente. Lança quando a identidade
    /// não pode ser resolvida — indica bug de configuração (endpoint
    /// acessível sem que a identidade tenha sido estabelecida antes), nunca
    /// um erro de negócio: por isso não é <c>Result&lt;Guid&gt;</c> (regra da
    /// seção 2.1 das convenções sobre exceção vs. <c>Result</c>).
    /// </summary>
    public Guid Id { get; }

    /// <summary>Indica se a identidade corrente pôde ser resolvida.</summary>
    public bool IsAuthenticated { get; }
}
