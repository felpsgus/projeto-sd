using TodoList.Identity.Application.Security;
using TodoList.Identity.Application.Sessions;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Users;
using TodoList.SharedKernel;

namespace TodoList.Identity.Application.Authentication;

/// <summary>
/// Caso de uso de login (BE-09/BE-33, RN-AUTH-08/RN-AUTH-09/RN-AUTH-10/RN-USER-04):
/// troca e-mail e senha por um access token e um refresh token de uma sessão
/// nova. O cookie é assunto do Gateway. BE-12: antes de verificar a senha, a
/// tentativa é contada atomicamente por e-mail normalizado (existente ou não);
/// acima de <see cref="LockoutOptions.MaxAttempts"/> devolve
/// <see cref="AuthErrors.TooManyAttempts"/> sem verificar a senha. Sucesso zera
/// o contador. E-mail malformado não conta (não há chave normalizada confiável)
/// e mantém o hash dummy.
///
/// <para>
/// <b>Um único caminho de retorno de falha.</b> E-mail vazio/malformado
/// (<see cref="Email.Create"/> falha), usuário inexistente e senha errada
/// devolvem exatamente o mesmo <see cref="AuthErrors.InvalidCredentials"/>
/// — nunca um erro diferente por causa, para não revelar por conteúdo da
/// resposta qual delas ocorreu (RN-AUTH-09). O caminho de e-mail
/// inexistente/malformado roda <c>Verify</c> contra o
/// <see cref="DummyPasswordHash"/> em vez de pular a verificação, para o
/// tempo de resposta não denunciar a causa (CA-05).
/// </para>
/// </summary>
public sealed class LoginHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly DummyPasswordHash _dummyPasswordHash;
    private readonly RefreshTokenService _refreshTokens;
    private readonly ILoginAttemptStore _attempts;
    private readonly LockoutOptions _lockout;
    private readonly TimeProvider _timeProvider;

    public LoginHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        DummyPasswordHash dummyPasswordHash,
        RefreshTokenService refreshTokens,
        ILoginAttemptStore attempts,
        LockoutOptions lockout,
        TimeProvider timeProvider)
    {
        _attempts = attempts;
        _lockout = lockout;
        _timeProvider = timeProvider;
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _dummyPasswordHash = dummyPasswordHash;
        _refreshTokens = refreshTokens;
    }

    /// <summary>Conta a tentativa; devolve o tempo restante se o e-mail está bloqueado, senão <c>null</c>.</summary>
    private async Task<TimeSpan?> RegisterAttemptAsync(string email, CancellationToken cancellationToken)
    {
        if (!_lockout.Enabled)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var lockoutDuration = TimeSpan.FromMinutes(_lockout.LockoutMinutes);
        var state = await _attempts.RegisterAttemptAsync(
            email, now, _lockout.MaxAttempts, lockoutDuration, TimeSpan.FromMinutes(_lockout.AttemptWindowMinutes), cancellationToken);

        if (state.Count <= _lockout.MaxAttempts)
        {
            return null;
        }

        var remaining = (state.LockedUntil ?? now + lockoutDuration) - now;

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// Autentica <paramref name="email"/>/<paramref name="password"/>. Nunca
    /// lança por conteúdo de entrada (CA-12 de BE-33) — inclusive
    /// nulo/vazio/malformado, que resultam no mesmo <see cref="AuthErrors.InvalidCredentials"/>
    /// de qualquer outra credencial inválida.
    /// </summary>
    public async Task<Result<LoginResult>> HandleAsync(string? email, string? password, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(email);
        var plainPassword = password ?? string.Empty;

        if (emailResult.IsFailure)
        {
            // Passo 2 (BE-33): paga o mesmo custo de CPU de um Verify real,
            // contra um hash fixo, mesmo sem e-mail válido para procurar.
            _passwordHasher.Verify(plainPassword, _dummyPasswordHash.Value);

            return Result.Failure<LoginResult>(AuthErrors.InvalidCredentials);
        }

        // BE-12: conta a tentativa ANTES de verificar a senha, num upsert atômico —
        // é isso que limita as verificações concorrentes a MaxAttempts (CA-10).
        if (await RegisterAttemptAsync(emailResult.Value.Value, cancellationToken) is { } retryAfter)
        {
            return Result.Failure<LoginResult>(AuthErrors.TooManyAttempts(retryAfter));
        }

        var user = await _userRepository.GetByEmailAsync(emailResult.Value, cancellationToken);

        if (user is null)
        {
            _passwordHasher.Verify(plainPassword, _dummyPasswordHash.Value);

            return Result.Failure<LoginResult>(AuthErrors.InvalidCredentials);
        }

        if (!_passwordHasher.Verify(plainPassword, user.PasswordHash))
        {
            return Result.Failure<LoginResult>(AuthErrors.InvalidCredentials);
        }

        if (_lockout.Enabled)
        {
            await _attempts.ResetAsync(emailResult.Value.Value, cancellationToken);
        }

        var accessToken = _tokenService.GenerateAccessToken(user);

        // BE-10/D-15: cada login abre uma sessão nova (sessionId nulo) — logins
        // simultâneos em dispositivos diferentes têm cadeias independentes.
        var refreshToken = await _refreshTokens.IssueAsync(user.Id, sessionId: null, cancellationToken);

        return Result.Success(new LoginResult(user.Id, accessToken, refreshToken));
    }
}

/// <summary>Sucesso de <see cref="LoginHandler.HandleAsync"/> (BE-33, BE-10).</summary>
/// <param name="UserId">Id do usuário autenticado.</param>
/// <param name="AccessToken">
/// Access token emitido (BE-08). O <c>ToString</c> deste record herda a
/// proteção de <see cref="Application.Security.AccessToken"/> — nunca imprime
/// o JWT, só a expiração (CA-06 de BE-33).
/// </param>
/// <param name="RefreshToken">Refresh token da sessão nova (BE-10); o <c>ToString</c> também omite o valor.</param>
public sealed record LoginResult(Guid UserId, AccessToken AccessToken, IssuedRefreshToken RefreshToken);
