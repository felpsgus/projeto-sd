using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>
/// Fábrica de tokens de teste (BE-40) — substitui o antigo padrão de
/// dublê de <c>ValidateToken</c>: agora que o Gateway valida localmente
/// (<c>AddJwtBearer</c>), os testes de CA-08 a CA-15 precisam de JWTs de
/// verdade, forjados aqui com controle total sobre algoritmo, chave e claims.
/// Nenhum destes tokens é versionado — a chave RSA usada vem de
/// <see cref="GatewayApiFactory.SigningKey"/>, gerada em memória, uma por
/// classe de teste.
/// </summary>
public static class JwtTestTokens
{
    /// <summary>Mesmo valor configurado em <see cref="GatewayApiFactory"/> (Jwt:Issuer).</summary>
    public const string DefaultIssuer = "todolist-identity";

    /// <summary>Mesmo valor configurado em <see cref="GatewayApiFactory"/> (Jwt:Audience).</summary>
    public const string DefaultAudience = "todolist";

    // SetDefaultTimesOnTokenCreation=false: sem isto, o handler sobrescreve
    // iat/nbf/exp pelo relógio do sistema mesmo quando o SecurityTokenDescriptor
    // já os informa explicitamente — o que quebraria justamente os cenários
    // que este helper existe para forjar (token expirado, mesma técnica de
    // JwtTokenService no Identity).
    private static readonly JsonWebTokenHandler _handler = new()
    {
        SetDefaultTimesOnTokenCreation = false,
    };

    /// <summary>Token RS256 válido, assinado pela chave configurada no Gateway (CA-13).</summary>
    public static string CreateValid(RSA signingKey, string subject) =>
        CreateRsa(signingKey, subject, DefaultIssuer, DefaultAudience, DateTime.UtcNow.AddMinutes(15));

    /// <summary>Token expirado há 1 segundo — CA-11 (ClockSkew zero, sem tolerância).</summary>
    public static string CreateExpired(RSA signingKey, string subject) =>
        CreateRsa(signingKey, subject, DefaultIssuer, DefaultAudience, DateTime.UtcNow.AddSeconds(-1));

    /// <summary>Token RS256 válido, mas com <c>iss</c> diferente do configurado no Gateway (CA-12).</summary>
    public static string CreateWithWrongIssuer(RSA signingKey, string subject) =>
        CreateRsa(signingKey, subject, "outro-issuer", DefaultAudience, DateTime.UtcNow.AddMinutes(15));

    /// <summary>Token RS256 válido, mas com <c>aud</c> diferente do configurado no Gateway (CA-12).</summary>
    public static string CreateWithWrongAudience(RSA signingKey, string subject) =>
        CreateRsa(signingKey, subject, DefaultIssuer, "outra-audience", DateTime.UtcNow.AddMinutes(15));

    /// <summary>
    /// Token RS256 sintaticamente válido, mas assinado por uma chave que não
    /// é a do Gateway (CA-10). A chave "outra" é gerada e usada uma única vez
    /// aqui dentro — nunca reaproveitada nem descartada enquanto o processo
    /// de teste seguir vivo (ver nota em <see cref="GatewayApiFactory.SigningKey"/>
    /// sobre por que descartar uma chave RSA logo após assinar quebra o cache
    /// de assinatura do Microsoft.IdentityModel).
    /// </summary>
    public static string CreateSignedByOtherKey(string subject) =>
        CreateRsa(RSA.Create(2048), subject, DefaultIssuer, DefaultAudience, DateTime.UtcNow.AddMinutes(15));

    /// <summary>Token HS256 (chave simétrica qualquer) — CA-08: recusado mesmo com claims corretos.</summary>
    public static string CreateHs256(string subject)
    {
        // 32+ bytes só para satisfazer o tamanho mínimo do algoritmo HMAC —
        // nunca reaproveitada como se fosse uma chave de produção.
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        return CreateToken(subject, DefaultIssuer, DefaultAudience, DateTime.UtcNow.AddMinutes(15), credentials);
    }

    /// <summary>Token com <c>alg=none</c> e sem assinatura — CA-09.</summary>
    public static string CreateNoneAlgorithm(string subject)
    {
        // Sem SigningCredentials, JsonWebTokenHandler.CreateToken escreve um
        // JWT não assinado (header alg=none) — exatamente o ataque clássico
        // que ValidAlgorithms=[RsaSha256]/RequireSignedTokens=true precisa
        // recusar (CA-09).
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = DefaultIssuer,
            Audience = DefaultAudience,
            IssuedAt = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = subject,
            },
        };

        return _handler.CreateToken(descriptor);
    }

    /// <summary>
    /// Nunca descarta <paramref name="key"/> — o cache de provedor de
    /// assinatura do Microsoft.IdentityModel é indexado pelo conteúdo da
    /// chave, não pela identidade do objeto; descartar um RSA logo após um
    /// único uso, enquanto outro código ainda referencia "a mesma" chave por
    /// conteúdo, produz <see cref="ObjectDisposedException"/> ou falha de
    /// verificação intermitente em chamadas futuras (mesmo padrão de chave
    /// persistente usado pelo Identity real, <c>RsaSigningKeyProvider</c>).
    /// </summary>
    private static string CreateRsa(RSA key, string subject, string issuer, string audience, DateTime expires)
    {
        var credentials = new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256);

        return CreateToken(subject, issuer, audience, expires, credentials);
    }

    private static string CreateToken(string subject, string issuer, string audience, DateTime expires, SigningCredentials credentials)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = DateTime.UtcNow,
            Expires = expires,
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = subject,
            },
        };

        return _handler.CreateToken(descriptor);
    }
}
