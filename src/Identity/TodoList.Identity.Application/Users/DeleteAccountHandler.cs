using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Persistence;
using TodoList.Identity.Application.Security;
using TodoList.SharedKernel;

namespace TodoList.Identity.Application.Users;

/// <summary>
/// Exclusão da própria conta (BE-16, RN-USER-05). Exige a senha atual (D-19,
/// "mesma proteção da troca de senha" — por isso reaproveita
/// <see cref="AuthErrors.InvalidCurrentPassword"/>, o mesmo código de
/// <see cref="ChangePasswordHandler"/>).
///
/// <para>
/// <b>A remoção das tarefas não é responsabilidade deste handler.</b> Ela
/// acontece em cascata, no banco, pela FK <c>tasks.tasks.owner_id →
/// identity.users(id) ON DELETE CASCADE</c> (D-27, já existente desde a
/// migration <c>AddOwnerForeignKeyToIdentityUsers</c> do Tasks Service) — este
/// handler não conhece nem chama o Tasks Service (D-27, CA-05b): nenhum RPC
/// novo de exclusão de tarefas foi adicionado ao contrato. A cascata roda na
/// mesma transação do <see cref="IUnitOfWork.SaveChangesAsync"/> abaixo,
/// porque é o mesmo banco — atomicidade preservada (CA-11) sem esforço extra.
/// </para>
///
/// <para>
/// <b>RN-AUTH-19 (BE-10).</b> Os refresh tokens do usuário também saem por
/// cascata: <c>identity.refresh_tokens.user_id → identity.users(id) ON DELETE
/// CASCADE</c>. Nenhum código extra aqui — o teste de integração de BE-10
/// prova que a cascata acontece. <c>login_attempts</c> (BE-12) entrará do
/// mesmo jeito.
/// </para>
/// </summary>
public sealed class DeleteAccountHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;

    public DeleteAccountHandler(IUserRepository userRepository, IUnitOfWork unitOfWork, IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
    }

    public async Task<Result> HandleAsync(Guid userId, string? password, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(AuthErrors.UserNotFound);
        }

        if (!_passwordHasher.Verify(password ?? string.Empty, user.PasswordHash))
        {
            // CA-10: nada é apagado — a checagem acontece antes de qualquer
            // Remove()/SaveChanges.
            return Result.Failure(AuthErrors.InvalidCurrentPassword);
        }

        _userRepository.Remove(user);

        // Um único SaveChangesAsync = uma única transação no Postgres: o
        // DELETE em identity.users e a cascata em tasks.tasks acontecem juntos
        // ou não acontecem (CA-11).
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
