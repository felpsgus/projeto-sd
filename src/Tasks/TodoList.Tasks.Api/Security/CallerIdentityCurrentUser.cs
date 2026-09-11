using TodoList.Tasks.Api.Grpc;
using TodoList.Tasks.Application.Security;

namespace TodoList.Tasks.Api.Security;

/// <summary>
/// Única implementação de <see cref="ICurrentUser"/> do Tasks Service
/// (BE-35, D-30 fechada) — lê o dono da tarefa da metadata gRPC
/// <c>x-user-id</c>, preenchida pelo API Gateway depois de validar o token do
/// usuário (D-34). Registrada direto em DI (<c>AddScoped&lt;ICurrentUser,
/// CallerIdentityCurrentUser&gt;()</c>), sem <c>if</c>/factory condicional
/// (CA-10): não existe mais um segundo modo a escolher.
///
/// <para>
/// <b>Não é mais provisório.</b> Até BE-29/D-30, esta classe (então
/// <c>HeaderCurrentUser</c>) só era ativada sob a flag
/// <c>Tasks:AllowAnonymousCreate=true</c>, um risco assumido e delimitado para
/// ambiente local/demo. A partir de BE-35, o Tasks confia no chamador de
/// forma permanente (D-34): como a metadata gRPC chega ao ASP.NET Core como
/// header HTTP/2 comum, a leitura via <see cref="IHttpContextAccessor"/> não
/// muda de mecanismo, só de nome de header (<c>X-User-Id</c> → <c>x-user-id</c>)
/// e de motivo — de "não há autenticação ainda" para "quem autentica é o
/// Gateway, este serviço não é a borda pública" (D-32).
/// </para>
///
/// <para>
/// <b>Por que isso é seguro só com o Tasks fora do alcance do navegador
/// (D-32).</b> Nenhuma validação de assinatura ou de token acontece aqui —
/// confiar no header sem isso só é aceitável porque o Tasks Service **não
/// deve** ser publicamente acessível: na VM a porta gRPC fica fechada no
/// firewall, e no Cloud Run o serviço é privado
/// (<c>--no-allow-unauthenticated</c>), alcançável apenas pela conta de
/// serviço do Gateway. Publicar este serviço sem essa barreira de rede
/// transformaria <c>x-user-id</c> em falsificação de identidade trivial.
/// </para>
///
/// <para>
/// <b>Por que <see cref="Id"/> pode confiar no header sem revalidar:</b> a
/// chamada só chega até o serviço gRPC depois de passar por
/// <see cref="RequireCallerIdentityInterceptor"/>, registrado só para
/// <c>TasksGrpcService</c> — o interceptor já garantiu que o header existe e é
/// um <see cref="Guid"/> válido antes do RPC ser invocado. Se <see cref="Id"/>
/// for acessado sem essa garantia (bug de fiação, não cenário de usuário), a
/// exceção abaixo é o sinal — nunca um erro genérico silencioso nem, pior, um
/// dono inventado.
/// </para>
/// </summary>
public sealed class CallerIdentityCurrentUser : ICurrentUser
{
    /// <summary>Nome da metadata gRPC / header HTTP/2 que carrega o dono da tarefa (D-34).</summary>
    public const string HeaderName = "x-user-id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CallerIdentityCurrentUser(IHttpContextAccessor httpContextAccessor)
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
                + $"fiação: o RPC deveria estar protegido por {nameof(RequireCallerIdentityInterceptor)} antes de "
                + "chegar aqui (BE-35).");
        }
    }

    public bool IsAuthenticated => TryGetHeaderUserId(out _);

    private bool TryGetHeaderUserId(out Guid id)
    {
        var header = _httpContextAccessor.HttpContext?.Request.Headers[HeaderName].ToString();

        return Guid.TryParse(header, out id);
    }
}
