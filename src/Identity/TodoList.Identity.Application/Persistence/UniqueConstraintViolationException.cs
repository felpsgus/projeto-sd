namespace TodoList.Identity.Application.Persistence;

/// <summary>
/// Sinaliza que <see cref="IUnitOfWork.SaveChangesAsync"/> falhou porque uma
/// restrição de unicidade do banco foi violada (BE-07, CA-12) — tipicamente o
/// índice único de <c>identity.users.email</c> (RN-AUTH-02) sob concorrência,
/// quando duas requisições de cadastro para o mesmo e-mail passam ambas pela
/// checagem prévia em memória (<c>IUserRepository.EmailExistsAsync</c>) antes
/// de uma delas commitar.
///
/// <para>
/// Vive na Application (não a exceção real do provider, ex.:
/// <c>Npgsql.PostgresException</c>) porque a Application não pode referenciar
/// Npgsql/EF Core (seção 2.1 das convenções, D-27) — só esta abstração. A
/// <c>IdentityDbContext</c> (Infrastructure) é quem detecta a violação real
/// (SQLSTATE <c>23505</c>) e a traduz para este tipo antes dela escapar do
/// <c>SaveChangesAsync</c>.
/// </para>
/// </summary>
public sealed class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException()
        : base("Violação de restrição de unicidade no banco.")
    {
    }

    public UniqueConstraintViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
