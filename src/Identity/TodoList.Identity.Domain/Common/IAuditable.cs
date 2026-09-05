namespace TodoList.Identity.Domain.Common;

/// <summary>
/// Entidade auditável (BE-02, CA-07/CA-08): <see cref="CreatedAt"/> e
/// <see cref="UpdatedAt"/> são preenchidos automaticamente pelo interceptor
/// de auditoria da Infrastructure a cada <c>SaveChangesAsync</c> — nunca
/// setados manualmente pelo código de aplicação ou de domínio.
/// </summary>
public interface IAuditable
{
    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
