using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TodoList.Gateway.Api.Configuration;

namespace TodoList.Gateway.Api.Authentication;

/// <summary>
/// Carrega a chave pública RSA de <see cref="JwtOptions.PublicKeyPath"/> uma
/// única vez por processo (BE-40, D-38) — singleton reaproveitado por toda
/// validação de token feita pelo middleware <c>AddJwtBearer</c> (ver
/// <see cref="ServiceCollectionExtensions"/>). Acessar <c>options.Value</c>
/// aqui dispara a mesma validação de <see cref="JwtOptions.Validate"/>
/// (<c>ValidateOnStart</c>) — se o host chegou até aqui sem ter subido, é
/// porque a validação já passou; o <see cref="RsaPublicKeyLoader.TryLoad"/>
/// abaixo é defesa em profundidade, não o mecanismo de CA-16 (esse é o
/// <c>ValidateOnStart</c> em <see cref="JwtOptions"/>). Mesmo espírito de
/// <c>RsaSigningKeyProvider</c> no Identity, do lado que só consome a chave.
/// </summary>
public sealed class JwtPublicKeyProvider : IDisposable
{
    private readonly RSA _rsa;

    /// <summary>Metade pública correspondente à chave privada do Identity (D-38) — só verifica, nunca assina.</summary>
    public RsaSecurityKey PublicKey { get; }

    public JwtPublicKeyProvider(IOptions<JwtOptions> options)
    {
        var jwtOptions = options.Value;

        var (rsa, failure) = RsaPublicKeyLoader.TryLoad(jwtOptions.PublicKeyPath);
        if (rsa is null)
        {
            throw new InvalidOperationException(
                $"Jwt:PublicKeyPath inválido ({failure}) — isto não deveria acontecer depois de ValidateOnStart.");
        }

        _rsa = rsa;
        PublicKey = new RsaSecurityKey(_rsa);
    }

    public void Dispose() => _rsa.Dispose();
}
