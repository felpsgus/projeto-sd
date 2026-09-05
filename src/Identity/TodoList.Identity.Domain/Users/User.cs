using System.Text.Json.Serialization;
using TodoList.SharedKernel;

namespace TodoList.Identity.Domain.Users;

/// <summary>
/// Entidade de usuário (BE-04, RN-USER-01). Todas as invariantes são
/// garantidas pela própria entidade, não pelo caso de uso: o construtor é
/// privado, a única forma de criar um <see cref="User"/> válido é <see cref="Create"/>,
/// e todo estado muda por método de domínio — <see cref="Rename"/>,
/// <see cref="Deactivate"/>, <see cref="ChangePasswordHash"/>. Nenhuma
/// propriedade expõe setter público (CA-07, verificado por teste de
/// reflection); <see cref="Email"/> não expõe setter nenhum, nem privado
/// (CA-08, RN-USER-03) — é passado só pelo construtor.
///
/// <para>
/// <b>Por que <see cref="CreatedAt"/>/<see cref="UpdatedAt"/> não usam o
/// interceptor de auditoria genérico (<c>IAuditable</c>, BE-02).</b> Esse
/// mecanismo therefore só atua em <c>SaveChanges(Async)</c> — não ajudaria o
/// teste de unidade puro de domínio que o CA-09 exige (<c>Rename</c> atualiza
/// <c>UpdatedAt</c>, verificável sem EF/banco). <see cref="User"/> por isso
/// mantém seu próprio relógio: cada método que muda estado recebe o mesmo
/// <see cref="TimeProvider"/> injetado no resto do processo (nunca
/// <c>DateTime.UtcNow</c> direto), o que também mantém os testes
/// determinísticos com um <c>FakeTimeProvider</c>.
/// </para>
///
/// <para>
/// <see cref="User"/> não implementa <c>ISoftDeletable</c>: <see cref="Deactivate"/>
/// (RN-USER-04) é um estado de negócio distinto de exclusão de conta
/// (RN-USER-05, BE-16, fora do escopo de BE-04) — um usuário inativo continua
/// existindo e sendo consultável, só não pode autenticar.
/// </para>
/// </summary>
public sealed class User
{
    /// <summary>RN-USER-01: nome de exibição tem entre 1 e 100 caracteres.</summary>
    public const int DisplayNameMaxLength = 100;

    /// <summary>
    /// EF materializa este tipo por <b>constructor binding</b> — não há
    /// construtor sem parâmetros nem propriedade com setter público, então o
    /// único jeito de reconstituir um <see cref="User"/> vindo do banco é por
    /// aqui, exatamente como o domínio constrói um novo (nota técnica de BE-04).
    /// </summary>
    private User(Guid id, Email email, string displayName, string passwordHash, bool isActive, DateTime createdAt, DateTime updatedAt)
    {
        Id = id;
        Email = email;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        IsActive = isActive;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Identificador único, gerado no domínio (RN-USER-01) — nunca pelo banco.</summary>
    public Guid Id { get; }

    /// <summary>Único, formato válido, imutável após a criação (RN-USER-01, RN-USER-03).</summary>
    public Email Email { get; }

    /// <summary>1–100 caracteres, editável via <see cref="Rename"/> (RN-USER-01, RN-USER-02).</summary>
    public string DisplayName { get; private set; }

    /// <summary>
    /// Hash da senha — nunca a senha em texto puro (RN-AUTH-05). O algoritmo
    /// de hashing real é BE-06; BE-04 só guarda e troca o que recebe.
    ///
    /// <para>
    /// <b><see cref="JsonIgnoreAttribute"/> é defesa em profundidade, não a
    /// regra principal.</b> A regra de verdade (RN-AUTH-05, convenção 2.1) é
    /// arquitetural: a API nunca serializa a entidade diretamente, só DTOs de
    /// resposta que simplesmente não têm este campo. O atributo aqui só
    /// garante que, mesmo que alguém quebre essa regra por engano no futuro
    /// (ex.: devolver <see cref="User"/> direto de um endpoint), o hash ainda
    /// não vaza (CA-12 de BE-04).
    /// </para>
    /// </summary>
    [JsonIgnore]
    public string PasswordHash { get; private set; }

    /// <summary>Nasce <c>true</c> (RN-AUTH-06); <c>false</c> depois de <see cref="Deactivate"/> (RN-USER-04).</summary>
    public bool IsActive { get; private set; }

    /// <summary>Preenchido na criação (UTC) — nunca muda depois (RN-USER-01).</summary>
    public DateTime CreatedAt { get; }

    /// <summary>Atualizado (UTC) a cada alteração de estado (RN-USER-01).</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Cria um usuário novo com id gerado pelo domínio (<see cref="Guid.NewGuid"/>).
    /// Este é o caminho normal de cadastro (BE-07) — a sobrecarga com
    /// <paramref name="email"/>/id explícito abaixo é só para os casos
    /// excepcionais em que o chamador precisa de um id conhecido (o seed de
    /// demonstração do BE-26, CA-14).
    /// </summary>
    public static Result<User> Create(Email email, string? displayName, string passwordHash, TimeProvider timeProvider) =>
        Create(Guid.NewGuid(), email, displayName, passwordHash, timeProvider);

    /// <summary>
    /// Cria um usuário novo com o <paramref name="id"/> informado. Se
    /// <paramref name="displayName"/> estiver ausente/vazio, usa a parte do
    /// e-mail antes do "@" (RN-AUTH-07). Nasce sempre <see cref="IsActive"/> =
    /// <c>true</c> (RN-AUTH-06, CA-06).
    /// </summary>
    public static Result<User> Create(Guid id, Email email, string? displayName, string passwordHash, TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return Result.Failure<User>(UserErrors.PasswordHashRequired);
        }

        var candidateDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? DisplayNameFromEmail(email)
            : displayName.Trim();

        var validatedDisplayName = ValidateDisplayName(candidateDisplayName);

        if (validatedDisplayName.IsFailure)
        {
            return Result.Failure<User>(validatedDisplayName.Error);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        return Result.Success(new User(id, email, validatedDisplayName.Value, passwordHash, isActive: true, now, now));
    }

    /// <summary>
    /// Altera o nome de exibição (RN-USER-02). Rejeita nome vazio, só espaços
    /// ou acima de <see cref="DisplayNameMaxLength"/> caracteres; quando
    /// aceito, atualiza <see cref="UpdatedAt"/> (CA-09).
    /// </summary>
    public Result Rename(string newDisplayName, TimeProvider timeProvider)
    {
        var validated = ValidateDisplayName(newDisplayName?.Trim() ?? string.Empty);

        if (validated.IsFailure)
        {
            return Result.Failure(validated.Error);
        }

        DisplayName = validated.Value;
        UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        return Result.Success();
    }

    /// <summary>
    /// Desativa o usuário (RN-USER-04): a partir daqui ele não deve mais
    /// conseguir autenticar-se — a checagem em si é do caso de uso de login
    /// (BE-09), aqui só o estado é registrado.
    /// </summary>
    public void Deactivate(TimeProvider timeProvider)
    {
        IsActive = false;
        UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
    }

    /// <summary>
    /// Troca o hash de senha armazenado (RN-AUTH-21). A força/validação da
    /// senha em si é BE-06 — aqui só o valor já com hash é aceito e guardado.
    /// </summary>
    public void ChangePasswordHash(string newPasswordHash, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);

        PasswordHash = newPasswordHash;
        UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
    }

    private static string DisplayNameFromEmail(Email email)
    {
        var atIndex = email.Value.IndexOf('@', StringComparison.Ordinal);

        return atIndex > 0 ? email.Value[..atIndex] : email.Value;
    }

    private static Result<string> ValidateDisplayName(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return Result.Failure<string>(UserErrors.DisplayNameEmpty);
        }

        if (candidate.Length > DisplayNameMaxLength)
        {
            return Result.Failure<string>(UserErrors.DisplayNameTooLong);
        }

        return Result.Success(candidate);
    }
}
