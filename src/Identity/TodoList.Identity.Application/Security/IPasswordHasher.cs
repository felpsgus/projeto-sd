namespace TodoList.Identity.Application.Security;

/// <summary>
/// Abstração de hashing de senha (BE-06, RN-AUTH-05). A implementação vive na
/// Infrastructure (<c>Pbkdf2PasswordHasher</c>) — a Application só conhece o
/// contrato, nunca o algoritmo de derivação, para poder trocá-lo sem tocar em
/// casos de uso.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Gera um hash auto-descritivo (algoritmo, custo e salt embutidos) a
    /// partir da senha em texto puro. Duas chamadas com a mesma
    /// <paramref name="plainPassword"/> produzem hashes diferentes (CA-01) —
    /// o salt é aleatório por chamada.
    /// </summary>
    public string Hash(string plainPassword);

    /// <summary>
    /// Confere se <paramref name="plainPassword"/> corresponde ao
    /// <paramref name="hash"/> informado. Um hash malformado, de algoritmo
    /// desconhecido ou de outra forma inválido resulta em <see langword="false"/> —
    /// nunca lança exceção (CA-03).
    /// </summary>
    public bool Verify(string plainPassword, string hash);
}
