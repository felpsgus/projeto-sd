using TodoList.Identity.Application.Persistence;
using TodoList.Identity.Application.Security;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Users;
using TodoList.SharedKernel;

namespace TodoList.Identity.Application.Authentication;

/// <summary>
/// Caso de uso de cadastro (BE-07, RN-AUTH-01/02/03/04/06/07). Assume que
/// <see cref="RegisterUserRequestValidator"/> já rodou antes (formato de
/// e-mail e política de senha) — mesmo assim revalida <see cref="Email"/> e a
/// política aqui (defesa em profundidade, e é o caminho exercido pelos testes
/// de unidade do handler, que não passam pelo validador).
///
/// <para>
/// <b>Duas camadas contra e-mail duplicado (RN-AUTH-02).</b> A checagem
/// prévia (<see cref="IUserRepository.EmailExistsAsync"/>) dá a mensagem
/// amigável no caminho comum; o índice único do banco
/// (<c>UserConfiguration</c>, BE-04) é a garantia sob concorrência. Quando as
/// duas requisições concorrentes passam pela checagem prévia antes de
/// qualquer uma commitar (CA-12), o <see cref="IUnitOfWork.SaveChangesAsync"/>
/// da perdedora lança <see cref="UniqueConstraintViolationException"/>
/// (traduzida pela <c>IdentityDbContext</c> a partir da violação real do
/// Postgres) — convertida aqui em <see cref="AuthErrors.EmailAlreadyRegistered"/>,
/// nunca uma exceção não tratada chegando à borda como 500.
/// </para>
/// </summary>
public sealed class RegisterUserHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly TimeProvider _timeProvider;

    public RegisterUserHandler(
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        TimeProvider timeProvider)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _timeProvider = timeProvider;
    }

    public async Task<Result<RegisterUserResult>> HandleAsync(RegisterUserRequest request, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(request.Email);

        if (emailResult.IsFailure)
        {
            return Result.Failure<RegisterUserResult>(emailResult.Error);
        }

        if (await _userRepository.EmailExistsAsync(emailResult.Value, cancellationToken))
        {
            return Result.Failure<RegisterUserResult>(AuthErrors.EmailAlreadyRegistered);
        }

        var passwordErrors = PasswordPolicy.Validate(request.Password);

        if (passwordErrors.Count > 0)
        {
            return Result.Failure<RegisterUserResult>(passwordErrors[0]);
        }

        var passwordHash = _passwordHasher.Hash(request.Password!);
        var userResult = User.Create(emailResult.Value, request.DisplayName, passwordHash, _timeProvider);

        if (userResult.IsFailure)
        {
            return Result.Failure<RegisterUserResult>(userResult.Error);
        }

        var user = userResult.Value;
        _userRepository.Add(user);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // CA-12: a corrida foi perdida — a outra requisição já commitou o
            // mesmo e-mail entre a checagem acima e este SaveChanges. O
            // cliente recebe Conflict, nunca uma exceção não tratada (500).
            return Result.Failure<RegisterUserResult>(AuthErrors.EmailAlreadyRegistered);
        }

        return Result.Success(new RegisterUserResult(user.Id, user.Email.Value, user.DisplayName, user.CreatedAt));
    }
}
