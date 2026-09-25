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
/// <b>Recorte da Fase 4.</b> RN-AUTH-19 pede remover também todos os refresh
/// tokens e registros de tentativa de login do usuário — as tabelas
/// <c>refresh_tokens</c> e <c>login_attempts</c> não existem nesta fase
/// (BE-10/BE-11/BE-12 são Fase 4), então esse passo fica de fora aqui,
/// deliberadamente. Quando essas tabelas existirem, com
/// <c>OnDelete(DeleteBehavior.Cascade)</c> de <c>User</c> para elas (nota
/// técnica de BE-16), a própria remoção do usuário já as levará junto — nenhum
/// código adicional neste handler deveria ser necessário.
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
