using TodoList.Tasks.Application.Security;

namespace TodoList.Tasks.Api.Security;

/// <summary>
/// Implementação provisória de <see cref="ICurrentUser"/> (BE-29, D-30): lê o
/// dono do header <c>X-User-Id</c>, em vez de um token — registrada em DI
/// apenas quando <c>Tasks:AllowAnonymousCreate=true</c> (ver
/// <c>Program.cs</c>). <b>Nunca</b> registrada no modo definitivo (CA-08):
/// nesse modo o container nem conhece esta classe, então o header não tem
/// como influenciar o dono da tarefa, mesmo enviado.
///
/// <para>
/// <b>Por que <see cref="Id"/> pode confiar no header sem revalidar:</b> a
/// rota só chega até aqui depois de passar por
/// <see cref="RequireValidUserIdHeaderFilter"/>, adicionado à mesma rota só
/// quando este modo está ligado — o filtro já garantiu que o header existe e
/// é um <see cref="Guid"/> válido antes do handler ser invocado. Se
/// <see cref="Id"/> for acessado sem essa garantia (bug de fiação, não
/// cenário de usuário), a exceção abaixo é o sinal — nunca um 500 genérico
/// silencioso nem, pior, um dono inventado.
/// </para>
///
/// <para>
/// <b>TODO</b> (dono: time Backend — BE-13; prazo: antes de qualquer
/// ambiente exposto fora de máquina local/demo): remover esta classe e o
/// modo <c>Tasks:AllowAnonymousCreate</c> por completo quando BE-13
/// implementar a identidade real (claim <c>sub</c> repassado pelo API
/// Gateway, D-31/D-32) no Tasks Service. O sinal de que isso aconteceu é
/// este endpoint sair da allowlist do teste de guarda de rotas (BE-13,
/// CA-07; ver BE-29, D-30 — "data de morte").
/// </para>
/// </summary>
public sealed class HeaderCurrentUser : ICurrentUser
{
    /// <summary>Nome do header que carrega o dono da tarefa neste modo provisório (D-30).</summary>
    public const string HeaderName = "X-User-Id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HeaderCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid Id
    {
        get
        {
            if (TryGetHeaderUserId(out var id))
            {
                return id;
            }

            throw new InvalidOperationException(
                $"ICurrentUser.Id foi acessado sem um '{HeaderName}' válido no HttpContext. Isso indica bug de "
                + $"fiação: a rota deveria estar protegida por {nameof(RequireValidUserIdHeaderFilter)} antes de "
                + "chegar aqui (BE-29).");
        }
    }

    public bool IsAuthenticated => TryGetHeaderUserId(out _);

    private bool TryGetHeaderUserId(out Guid id)
    {
        var header = _httpContextAccessor.HttpContext?.Request.Headers[HeaderName].ToString();

        return Guid.TryParse(header, out id);
    }
}
