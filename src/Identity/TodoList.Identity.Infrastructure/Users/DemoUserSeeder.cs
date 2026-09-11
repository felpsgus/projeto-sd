using Microsoft.Extensions.Logging;
using TodoList.Identity.Application.Persistence;
using TodoList.Identity.Application.Security;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Users;

namespace TodoList.Identity.Infrastructure.Users;

/// <summary>
/// Popula <c>identity.users</c> com os dois usuários de demonstração,
/// usando os <b>mesmos ids fixos</b> já usados por <see cref="InMemoryUserLookup"/>
/// (<see cref="InMemoryUserLookup.ActiveUserId"/>/<see cref="InMemoryUserLookup.InactiveUserId"/>)
/// — a FK cruzada <c>tasks.tasks.owner_id → identity.users(id)</c> (nota
/// registrada no README) só funciona se esses dois ids existirem de verdade
/// no banco, não só no seed em memória do gRPC.
///
/// <para>
/// <b>Desligado por padrão</b> (<c>UserStore:SeedDemoUsers</c>, default
/// <c>false</c>): só roda quando ligado explicitamente em configuração — nunca
/// em produção.
/// </para>
///
/// <para>
/// <b>Senha real, sincronizada a cada execução (BE-33).</b> A senha de
/// demonstração chega por parâmetro de <see cref="SeedAsync"/> — nunca lida
/// de <c>UserStoreOptions</c> diretamente, porque essa opção vive em
/// <c>TodoList.Identity.Api</c> e a Infrastructure não pode referenciar a Api
/// (a dependência aponta sempre para dentro, seção 2.1 das convenções). Quem
/// resolve a senha e decide se o seed roda é <c>Program.cs</c>. Para um
/// usuário novo, o hash vem de <see cref="IPasswordHasher.Hash"/>. Para um
/// usuário que já existe, o seed só regrava o hash se
/// <see cref="IPasswordHasher.Verify"/> contra a senha atual falhar — o que
/// cobre tanto o hash placeholder legado do T1 (nunca passa em
/// <c>Verify</c>, BE-06 CA-03) quanto uma troca de <c>DemoUserPassword</c>
/// entre implantações — e não escreve nada quando a senha já confere
/// (idempotente, CA-09 de BE-33). O estado ativo/inativo do usuário existente
/// nunca é tocado por este método.
/// </para>
/// </summary>
public sealed partial class DemoUserSeeder
{
    private const string ActiveUserEmail = "ada.lovelace@todolist.example";
    private const string ActiveUserDisplayName = "Ada Lovelace";
    private const string InactiveUserEmail = "charles.babbage@todolist.example";
    private const string InactiveUserDisplayName = "Charles Babbage";

    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DemoUserSeeder> _logger;

    public DemoUserSeeder(
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        TimeProvider timeProvider,
        ILogger<DemoUserSeeder> logger)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Semeia (ou sincroniza a senha de) os dois usuários de demonstração.
    /// </summary>
    /// <param name="demoPassword">
    /// Senha em texto puro (<c>UserStore:DemoUserPassword</c>, resolvida pela
    /// Api) — nunca logada, nunca persistida como tal, só o hash.
    /// </param>
    public async Task SeedAsync(string demoPassword, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(demoPassword);

        Log.SeedingDemoUsers(_logger, InMemoryUserLookup.ActiveUserId, InMemoryUserLookup.InactiveUserId);

        var changedAny = false;
        changedAny |= await EnsureUserAsync(
            InMemoryUserLookup.ActiveUserId, ActiveUserEmail, ActiveUserDisplayName, isActive: true, demoPassword, cancellationToken);
        changedAny |= await EnsureUserAsync(
            InMemoryUserLookup.InactiveUserId, InactiveUserEmail, InactiveUserDisplayName, isActive: false, demoPassword, cancellationToken);

        if (changedAny)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<bool> EnsureUserAsync(
        Guid id, string email, string displayName, bool isActive, string demoPassword, CancellationToken cancellationToken)
    {
        var existing = await _userRepository.GetByIdAsync(id, cancellationToken);

        if (existing is null)
        {
            var emailResult = Email.Create(email);
            var userResult = User.Create(id, emailResult.Value, displayName, _passwordHasher.Hash(demoPassword), _timeProvider);
            var user = userResult.Value;

            if (!isActive)
            {
                user.Deactivate(_timeProvider);
            }

            _userRepository.Add(user);

            return true;
        }

        // Idempotência (CA-09 de BE-33): só regrava se a senha atual não bater
        // mais com a de demonstração — cobre o hash placeholder do T1 (nunca
        // verifica) e a troca de DemoUserPassword entre implantações. Estado
        // ativo/inativo nunca é tocado aqui.
        if (_passwordHasher.Verify(demoPassword, existing.PasswordHash))
        {
            return false;
        }

        existing.ChangePasswordHash(_passwordHasher.Hash(demoPassword), _timeProvider);

        return true;
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Identity está SEMEANDO usuários de demonstração em identity.users " +
                "(UserStore:SeedDemoUsers=true) — nunca ligue isto em produção. Ids fixos: " +
                "{ActiveUserId} (ativo), {InactiveUserId} (inativo).")]
        public static partial void SeedingDemoUsers(ILogger logger, Guid activeUserId, Guid inactiveUserId);
    }
}
