using System.ComponentModel.DataAnnotations;

namespace TodoList.Identity.Api.Configuration;

/// <summary>
/// Seleciona, por configuração, qual implementação de <c>IUserLookup</c> o
/// Identity usa (BE-26) — <see cref="InMemoryProvider"/> (seed fixo em
/// memória) ou <see cref="PersistedProvider"/> (banco real, via
/// <c>PersistedUserLookup</c>/BE-04, CA-13 de BE-26) — e se o seed de
/// usuários de demonstração roda no banco (<see cref="SeedDemoUsers"/>).
/// </summary>
public sealed class UserStoreOptions
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
    /// <c>tasks.tasks.owner_id</c> funcionar na demonstração do T1, não
    /// selecionar de onde <c>ValidateUser</c> lê. <b>Desligado por padrão</b>
    /// — nunca ligue em produção.
    /// </summary>
    public bool SeedDemoUsers { get; init; }
}
