using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TodoList.Identity.Application.Security;
using TodoList.Identity.Domain.Users;

namespace TodoList.Identity.Infrastructure.Security;

/// <summary>
/// Implementação de <see cref="ITokenService"/> (BE-08) com
/// <see cref="JsonWebTokenHandler"/>, RS256 e a chave privada de
/// <see cref="RsaSigningKeyProvider"/> (D-38 — a chave privada nunca sai
/// deste serviço; só a pública é distribuída, ao Gateway). Sem estado
/// mutável (a chave é lida uma vez, no singleton <see cref="RsaSigningKeyProvider"/>)
/// e thread-safe — registrado como singleton (ver <see cref="ServiceCollectionExtensions"/>).
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    private static readonly JsonWebTokenHandler _handler = new()
    {
        // Armadilha de tempo (nota técnica de BE-08): sem isto, o handler
        // preenche iat/nbf/exp pelo relógio do sistema mesmo quando o
        // SecurityTokenDescriptor já os informa explicitamente. Todo cálculo
        // de tempo abaixo vem do TimeProvider injetado, nunca do relógio real
        // — é o que permite o CA-06/CA-07 serem testados com FakeTimeProvider.
        SetDefaultTimesOnTokenCreation = false,
    };

    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider, RsaSigningKeyProvider signingKeyProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;

        // kid no header (CA-01/CA-02 de BE-40): SigningCredentials com uma
        // RsaSecurityKey cujo KeyId já vem preenchido (RsaSigningKeyProvider)
        // faz o JsonWebTokenHandler escrever "kid" automaticamente — nenhum
        // código aqui precisa tocar no header manualmente.
        _signingCredentials = new SigningCredentials(signingKeyProvider.PrivateKey, SecurityAlgorithms.RsaSha256);
    }

    public AccessToken GenerateAccessToken(User user)
    {
        var issuedAt = _timeProvider.GetUtcNow();
        var expiresAt = issuedAt.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,

            // NotBefore é propositalmente NÃO informado: se fosse, o handler
            // acrescentaria um claim "nbf" ao token — e o conjunto de claims
            // exigido por CA-04/CA-05/CA-11 é exatamente sub/email/jti/iat/
            // exp/iss/aud, "nada mais".
            SigningCredentials = _signingCredentials,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email.Value,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(), // CA-11: novo a cada chamada
            },
        };

        var token = _handler.CreateToken(descriptor);

        return new AccessToken(token, expiresAt);
    }
}
