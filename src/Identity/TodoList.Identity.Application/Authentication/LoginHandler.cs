using TodoList.Identity.Application.Security;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Users;
using TodoList.SharedKernel;

namespace TodoList.Identity.Application.Authentication;

/// <summary>
/// Caso de uso de login (BE-33, RN-AUTH-08/RN-AUTH-09/RN-USER-04) — recorte de
/// BE-09 para o T2 (D-36): troca e-mail e senha por um access token, sem
/// refresh token, cookie ou bloqueio por tentativas.
///
/// <para>
/// <b>Um único caminho de retorno de falha.</b> E-mail vazio/malformado
/// (<see cref="Email.Create"/> falha), usuário inexistente, senha errada e
/// usuário inativo devolvem exatamente o mesmo <see cref="AuthErrors.InvalidCredentials"/>
/// — nunca um erro diferente por causa, para não revelar por conteúdo da
/// resposta qual delas ocorreu (RN-AUTH-09). A checagem de <see cref="User.IsActive"/>
/// vem <b>depois</b> de <see cref="IPasswordHasher.Verify"/>, para o tempo de
/// resposta também não denunciar usuário inativo — mesma exigência para o
/// caminho de e-mail inexistente/malformado, que roda <c>Verify</c> contra o
/// <see cref="DummyPasswordHash"/> em vez de pular a verificação (CA-05).
/// </para>
/// </summary>
public sealed class LoginHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly DummyPasswordHash _dummyPasswordHash;

    public LoginHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        DummyPasswordHash dummyPasswordHash)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _dummyPasswordHash = dummyPasswordHash;
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

        // RN-USER-04: checado só depois do Verify, para o tempo de resposta
        // não denunciar "usuário inativo" (CA-04/CA-05 de BE-33).
        if (!user.IsActive)
        {
            return Result.Failure<LoginResult>(AuthErrors.InvalidCredentials);
        }

        var accessToken = _tokenService.GenerateAccessToken(user);

        return Result.Success(new LoginResult(user.Id, accessToken));
    }
}

/// <summary>Sucesso de <see cref="LoginHandler.HandleAsync"/> (BE-33).</summary>
/// <param name="UserId">Id do usuário autenticado.</param>
/// <param name="AccessToken">
/// Access token emitido (BE-08). O <c>ToString</c> deste record herda a
/// proteção de <see cref="Application.Security.AccessToken"/> — nunca imprime
/// o JWT, só a expiração (CA-06 de BE-33).
/// </param>
public sealed record LoginResult(Guid UserId, AccessToken AccessToken);
