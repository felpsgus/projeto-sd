using Microsoft.Extensions.Logging;
using TodoList.Identity.Application.Persistence;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Users;

namespace TodoList.Identity.Infrastructure.Users;

/// <summary>
/// Popula <c>identity.users</c> com os dois usuários de demonstração do T1,
/// usando os <b>mesmos ids fixos</b> já usados por <see cref="InMemoryUserLookup"/>
/// (<see cref="InMemoryUserLookup.ActiveUserId"/>/<see cref="InMemoryUserLookup.InactiveUserId"/>)
/// — a FK cruzada <c>tasks.tasks.owner_id → identity.users(id)</c> (nota
/// registrada no README) só funciona se esses dois ids existirem de verdade
/// no banco, não só no seed em memória do gRPC.
///
/// <para>
/// <b>Desligado por padrão</b> (<c>UserStore:SeedDemoUsers</c>, default
/// <c>false</c>): só roda quando ligado explicitamente em configuração — nunca
/// em produção. Idempotente: cada usuário só é inserido se ainda não existe
/// com aquele id (checagem por <see cref="IUserRepository.GetByIdAsync"/>),
/// então rodar de novo (ex.: reiniciar o serviço com a flag ligada) não
/// duplica nem falha. Nunca chama <c>EnsureCreated()</c>/<c>Migrate()</c> —
/// pressupõe que a migration do BE-04 já foi aplicada (ver README).
/// </para>
///
/// <para>
/// <b><see cref="PlaceholderPasswordHash"/> não é um hash válido.</b> A
/// derivação de hash real de senha é BE-06; até lá, os usuários de
/// demonstração recebem este valor fixo só para satisfazer a coluna
/// obrigatória — nenhum destes usuários consegue autenticar de verdade
/// enquanto BE-06/BE-09 não estiverem prontos, e isso é esperado.
/// </para>
/// </summary>
public sealed partial class DemoUserSeeder
{
    /// <summary>
    /// NÃO é um hash de senha válido — placeholder documentado (ver
    /// resumo da classe). BE-06 substitui por hashing real.
    /// </summary>
    internal const string PlaceholderPasswordHash = "seed-placeholder-not-a-real-hash-be-06-pending";

    private const string ActiveUserEmail = "ada.lovelace@todolist.example";
    private const string ActiveUserDisplayName = "Ada Lovelace";
    private const string InactiveUserEmail = "charles.babbage@todolist.example";
    private const string InactiveUserDisplayName = "Charles Babbage";

    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DemoUserSeeder> _logger;

    public DemoUserSeeder(IUserRepository userRepository, IUnitOfWork unitOfWork, TimeProvider timeProvider, ILogger<DemoUserSeeder> logger)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        Log.SeedingDemoUsers(_logger, InMemoryUserLookup.ActiveUserId, InMemoryUserLookup.InactiveUserId);

        var addedAny = false;
        addedAny |= await EnsureUserAsync(
            InMemoryUserLookup.ActiveUserId, ActiveUserEmail, ActiveUserDisplayName, isActive: true, cancellationToken);
        addedAny |= await EnsureUserAsync(
            InMemoryUserLookup.InactiveUserId, InactiveUserEmail, InactiveUserDisplayName, isActive: false, cancellationToken);

        if (addedAny)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<bool> EnsureUserAsync(Guid id, string email, string displayName, bool isActive, CancellationToken cancellationToken)
    {
        var existing = await _userRepository.GetByIdAsync(id, cancellationToken);

        if (existing is not null)
        {
            // Idempotência: já existe (rodada anterior do seed) — não duplica.
            return false;
        }

        var emailResult = Email.Create(email);
        var userResult = User.Create(id, emailResult.Value, displayName, PlaceholderPasswordHash, _timeProvider);
        var user = userResult.Value;

        if (!isActive)
        {
            user.Deactivate(_timeProvider);
        }

        _userRepository.Add(user);

        return true;
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Identity está SEMEANDO usuários de demonstração em identity.users " +
                "(UserStore:SeedDemoUsers=true) — nunca ligue isto em produção. Ids fixos: " +
                "{ActiveUserId} (ativo), {InactiveUserId} (inativo). PasswordHash é um placeholder, " +
                "não um hash válido (BE-06 substitui).")]
        public static partial void SeedingDemoUsers(ILogger logger, Guid activeUserId, Guid inactiveUserId);
    }
}
