namespace TodoList.Identity.Domain.Sessions;

/// <summary>
/// Por que um refresh token foi revogado (BE-10). Persistido como texto — o
/// nome do membro é o valor da coluna <c>revoked_reason</c>, então renomear um
/// membro exige migration de dados.
/// </summary>
public enum RefreshTokenRevocationReason
{
    /// <summary>Logout explícito (RN-AUTH-12).</summary>
    Logout,

    /// <summary>Troca de senha (RN-AUTH-19, RN-AUTH-21).</summary>
    PasswordChanged,

    /// <summary>Token já consumido apresentado de novo — vazamento presumido (RN-AUTH-17).</summary>
    ReuseDetected,

    /// <summary>Usuário desativado depois de emitida a sessão (RN-USER-04, RN-AUTH-19).</summary>
    AccountDeactivated,
}
