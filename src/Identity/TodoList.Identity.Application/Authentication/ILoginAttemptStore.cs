namespace TodoList.Identity.Application.Authentication;

/// <summary>
/// Contador persistente de tentativas de login por e-mail normalizado (BE-12).
/// A chave não é FK para usuários: e-mails inexistentes contam igual (CA-08, ADR 0002).
/// </summary>
public interface ILoginAttemptStore
{
    /// <summary>
    /// Registra <b>atomicamente</b> uma tentativa (um único upsert no banco — CA-10) e devolve o
    /// estado resultante. A tentativa é contada antes da verificação de senha; quem recebe
    /// <see cref="LoginAttemptState.Count"/> acima de <paramref name="maxAttempts"/> foi
    /// recusado por bloqueio. Bloqueio expirado ou janela vencida recomeçam a contagem em 1.
    /// Durante o bloqueio o contador continua subindo, mas <c>LockedUntil</c> não se move.
    /// </summary>
    public Task<LoginAttemptState> RegisterAttemptAsync(
        string email, DateTime now, int maxAttempts, TimeSpan lockout, TimeSpan window, CancellationToken cancellationToken);

    /// <summary>Login bem-sucedido: apaga o contador e o bloqueio (CA-05).</summary>
    public Task ResetAsync(string email, CancellationToken cancellationToken);
}

/// <summary>Estado após <see cref="ILoginAttemptStore.RegisterAttemptAsync"/>.</summary>
public readonly record struct LoginAttemptState(int Count, DateTime? LockedUntil);
