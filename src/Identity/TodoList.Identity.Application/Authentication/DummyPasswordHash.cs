using TodoList.Identity.Application.Security;

namespace TodoList.Identity.Application.Authentication;

/// <summary>
/// Hash dummy fixo, gerado uma única vez por processo, usado por
/// <see cref="LoginHandler"/> para pagar o mesmo custo de CPU de
/// <see cref="IPasswordHasher.Verify"/> quando não há usuário real contra quem
/// verificar (BE-33, RN-AUTH-09) — sem isso, "e-mail inexistente" responderia
/// mais rápido que "senha errada", e o tempo de resposta denunciaria a causa
/// da falha (CA-05 de BE-33).
///
/// <para>
/// <b>Por que um tipo próprio, singleton, e não um campo do handler.</b>
/// <see cref="LoginHandler"/> depende de <c>IUserRepository</c>, que por sua
/// vez depende do <c>DbContext</c> — <c>Scoped</c>. Um hash calculado uma vez
/// "por processo" não pode viver num serviço <c>Scoped</c> sem recalcular a
/// cada requisição (o próprio objetivo de custo fixo se perderia). Este tipo
/// depende só de <see cref="IPasswordHasher"/>, que é singleton
/// (<c>Pbkdf2PasswordHasher</c>) — registrar <see cref="DummyPasswordHash"/>
/// como singleton não cria dependência cativa nenhuma, e o <see cref="Lazy{T}"/>
/// garante que o hash só é calculado na primeira chamada, não na
/// inicialização do processo.
/// </para>
/// </summary>
public sealed class DummyPasswordHash
{
    private readonly Lazy<string> _hash;

    public DummyPasswordHash(IPasswordHasher passwordHasher)
    {
        // A senha "real" por trás deste hash não importa — nunca é comparada
        // contra nada, só existe para o Verify ter o mesmo custo do caminho
        // de sucesso. Guid aleatório evita qualquer string mágica no código.
        _hash = new Lazy<string>(() => passwordHasher.Hash(Guid.NewGuid().ToString("N")));
    }

    /// <summary>O hash dummy — mesmo valor durante toda a vida do processo.</summary>
    public string Value => _hash.Value;
}
