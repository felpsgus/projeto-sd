using Microsoft.Extensions.Logging;
using TodoList.Identity.Application.Users;

namespace TodoList.Identity.Infrastructure.Users;

/// <summary>
/// Implementação de demonstração de <see cref="IUserLookup"/>, seleção via
/// <c>UserStore:Provider = "InMemory"</c> enquanto a persistência de usuário
/// (BE-04/BE-07) não estiver ligada. Roda no processo real — não é mock de
/// teste — e existe exatamente um usuário fixo (CA-14 de BE-26), com o id
/// documentado no README.
/// </summary>
public sealed partial class InMemoryUserLookup : IUserLookup
{
    /// <summary>Usuário do seed — ver README (seção "Seed de usuários").</summary>
    public static readonly Guid SeedUserId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    private readonly Dictionary<Guid, UserLookupResult> _users;

    public InMemoryUserLookup(ILogger<InMemoryUserLookup> logger)
    {
        _users = new Dictionary<Guid, UserLookupResult>
        {
            [SeedUserId] = new UserLookupResult(DisplayName: "Ada Lovelace"),
        };

        // CA-15 — deixa explícito, na inicialização, que o store persistido
        // não está em uso: quem ler o log não pode achar que ValidateUser
        // reflete o banco quando na verdade reflete este seed fixo.
        Log.UsingInMemorySeed(logger, SeedUserId);
    }

    public Task<UserLookupResult?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_users.GetValueOrDefault(userId));

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Identity está usando o seed de usuários EM MEMÓRIA (UserStore:Provider=InMemory) — " +
                "o store persistido (BE-04/BE-07) não está em uso. Usuário fixo: {SeedUserId}.")]
        public static partial void UsingInMemorySeed(ILogger logger, Guid seedUserId);
    }
}
