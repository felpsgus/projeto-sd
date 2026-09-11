using System.ComponentModel.DataAnnotations;

namespace TodoList.Identity.Api.Configuration;

/// <summary>
/// Seleciona, por configuração, qual implementação de <c>IUserLookup</c> o
/// Identity usa (BE-26) — <see cref="InMemoryProvider"/> (seed fixo em
/// memória) ou <see cref="PersistedProvider"/> (banco real, via
/// <c>PersistedUserLookup</c>/BE-04, CA-13 de BE-26) — e se o seed de
/// usuários de demonstração roda no banco (<see cref="SeedDemoUsers"/>).
/// </summary>
public sealed class UserStoreOptions : IValidatableObject
{
    public const string SectionName = "UserStore";

    public const string InMemoryProvider = "InMemory";
    public const string PersistedProvider = "Persisted";

    [Required(AllowEmptyStrings = false)]
    public string Provider { get; init; } = InMemoryProvider;

    /// <summary>
    /// Liga o seed de usuários de demonstração (<c>DemoUserSeeder</c>) no
    /// banco real do Identity — independente de <see cref="Provider"/>, já que
    /// o objetivo é popular <c>identity.users</c> para a FK cruzada
    /// <c>tasks.tasks.owner_id</c> funcionar na demonstração, não selecionar
    /// de onde <c>ValidateUser</c> lê. <b>Desligado por padrão</b> — nunca
    /// ligue em produção.
    /// </summary>
    public bool SeedDemoUsers { get; init; }

    /// <summary>
    /// Senha em texto puro dos dois usuários de demonstração (BE-33) — lida
    /// só na inicialização, nunca versionada (mesmo padrão de
    /// <c>Jwt:SigningKey</c>, BE-08). Obrigatória quando
    /// <see cref="SeedDemoUsers"/> é <see langword="true"/> — sem ela, o
    /// serviço não sobe com seed ligado (<see cref="Validate"/>), para não
    /// semear silenciosamente usuários que ninguém consegue autenticar.
    /// </summary>
    public string? DemoUserPassword { get; init; }

    /// <summary>
    /// CA-07 de BE-33: <see cref="DemoUserPassword"/> ausente com
    /// <see cref="SeedDemoUsers"/> ligado falha a inicialização com uma
    /// mensagem que nomeia <c>UserStore:DemoUserPassword</c> — o valor da
    /// senha nunca aparece na mensagem.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SeedDemoUsers && string.IsNullOrEmpty(DemoUserPassword))
        {
            yield return new ValidationResult(
                "UserStore:DemoUserPassword é obrigatória quando UserStore:SeedDemoUsers=true " +
                "(variável de ambiente/secret — nunca versionada).",
                [nameof(DemoUserPassword)]);
        }
    }
}
