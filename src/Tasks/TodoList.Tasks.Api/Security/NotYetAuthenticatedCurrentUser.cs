using TodoList.Tasks.Application.Security;

namespace TodoList.Tasks.Api.Security;

/// <summary>
/// Implementação de <see cref="ICurrentUser"/> registrada quando
/// <c>Tasks:AllowAnonymousCreate=false</c> (o padrão, modo definitivo) —
/// enquanto BE-13 não chega ao Tasks Service, é o que impede o serviço de
/// aceitar qualquer chamador silenciosamente.
///
/// <para>
/// <b>Por que não é um 401 "de mentira".</b> BE-13 (autenticação, verificação
/// de token, resposta 401) está fora do escopo de BE-17/BE-28/BE-29 — não há
/// ainda mecanismo nenhum para saber quem está chamando no modo definitivo.
/// Fabricar um 401 aqui simularia uma proteção que não existe. Em vez disso,
/// <see cref="Id"/> lança de forma explícita: o pedido nunca é silenciosamente
/// aceito em nome de ninguém, e a falha é óbvia (visível em log e na resposta
/// 500 genérica do <c>GlobalExceptionHandler</c>) em vez de mascarada.
/// </para>
///
/// <para>
/// <b>TODO</b> (dono: time Backend — BE-13; prazo: antes de qualquer ambiente
/// exposto fora de máquina local/demo): substituir esta classe pela
/// implementação real de <see cref="ICurrentUser"/> que lê o claim
/// <c>sub</c> do token repassado pelo API Gateway (D-31/D-32), assim que
/// BE-13 chegar ao Tasks Service. Quando isso acontecer, <c>POST /api/tasks</c>
/// sem token passa a responder <b>401</b> (BE-17 CA-21, BE-29 CA-07) —
/// critério que esta base de código ainda não fecha (ver relatório de
/// BE-17/BE-28/BE-29).
/// </para>
/// </summary>
public sealed class NotYetAuthenticatedCurrentUser : ICurrentUser
{
    public Guid Id =>
        throw new InvalidOperationException(
            "ICurrentUser.Id foi acessado com Tasks:AllowAnonymousCreate=false, mas o Tasks Service ainda não "
            + "valida autenticação (BE-13 não implementado nesta etapa — ver BE-17/BE-28/BE-29). Não há dono a "
            + "resolver: nenhuma tarefa é criada em nome de um chamador desconhecido. Ligue "
            + "Tasks:AllowAnonymousCreate=true (com X-User-Id) para o gatilho provisório de demonstração, ou "
            + "implemente BE-13 antes de expor este endpoint.");

    public bool IsAuthenticated => false;
}
