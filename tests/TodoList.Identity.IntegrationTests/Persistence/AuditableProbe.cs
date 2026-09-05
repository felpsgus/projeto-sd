using TodoList.Identity.Domain.Common;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// Entidade de teste (BE-02, "Testes obrigatórios"): não existe nenhuma
/// entidade de negócio ainda (User é BE-04), então o CRUD trivial que exercita
/// CA-05/CA-06/CA-07 precisa de uma. Vive só neste projeto de teste — nunca
/// em produção — mas implementa as mesmas interfaces de domínio
/// (<see cref="IAuditable"/>, <see cref="ISoftDeletable"/>) que uma entidade
/// real do Identity vai implementar a partir de BE-04.
/// </summary>
public sealed class AuditableProbe : IAuditable, ISoftDeletable
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// DateTime "de negócio": quem escreve é o chamador, não o interceptor de
    /// auditoria. É por aqui que o CA-05 tem dentes. CreatedAt/UpdatedAt já
    /// nascem <c>Kind=Utc</c> do <c>TimeProvider</c>, e o Npgsql devolve isso
    /// corretamente mesmo com a convenção UTC desligada — asserção só sobre
    /// eles não provaria que a convenção do projeto está aplicada. Um valor
    /// <c>Kind=Local</c> só volta certo se ela estiver.
    /// </summary>
    public DateTime OccurredAt { get; set; }
}
