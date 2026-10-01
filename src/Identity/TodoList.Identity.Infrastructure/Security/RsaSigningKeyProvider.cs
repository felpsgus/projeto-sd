using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace TodoList.Identity.Infrastructure.Security;

/// <summary>
/// Carrega a chave RSA de <see cref="JwtOptions.PrivateKeyPath"/> uma única
/// vez por processo (BE-40, D-38) e expõe a chave privada, usada só por
/// <see cref="JwtTokenService"/> para assinar, e o <see cref="KeyId"/>: o
/// thumbprint JWK RFC 7638 (SHA-256 sobre a forma canônica <c>{e, kty, n}</c>)
/// da chave pública — derivada via <see cref="RSA.ExportParameters(bool)"/>
/// com <c>includePrivateParameters: false</c> só para esse cálculo —, em
/// Base64Url, calculado uma vez aqui e reaproveitado em todo token emitido
/// (CA-01/CA-02 de BE-40). A validação é do Gateway, que tem só a metade
/// pública (arquivo PEM próprio). Registrado como singleton (ver <see cref="ServiceCollectionExtensions"/>):
/// sem estado mutável após a construção, e liberar/reabrir o PEM a cada
/// requisição não teria propósito — a chave não muda em vida do processo
/// (sem rotação nesta etapa, ver nota técnica de BE-40).
/// </summary>
public sealed class RsaSigningKeyProvider : IDisposable
{
    private readonly RSA _privateRsa;

    /// <summary>Metade privada — só para assinar (<see cref="JwtTokenService"/>).</summary>
    public RsaSecurityKey PrivateKey { get; }

    /// <summary>Thumbprint JWK RFC 7638 da chave pública, em Base64Url — um único <c>kid</c> por processo.</summary>
    public string KeyId { get; }

    public RsaSigningKeyProvider(IOptions<JwtOptions> options)
    {
        // Acessar .Value aqui dispara a mesma validação de
        // JwtOptions.Validate (ValidateOnStart) — se o host chegou até aqui
        // sem ter subido, é porque a validação já passou. O TryLoad abaixo é
        // defesa em profundidade, não o mecanismo de CA-03 a CA-06 (esse é o
        // ValidateOnStart em JwtOptions).
        var jwtOptions = options.Value;

        var (rsa, failure) = RsaPrivateKeyLoader.TryLoad(jwtOptions.PrivateKeyPath);
        if (rsa is null)
        {
            throw new InvalidOperationException(
                $"Jwt:PrivateKeyPath inválido ({failure}) — isto não deveria acontecer depois de ValidateOnStart.");
        }

        _privateRsa = rsa;

        var publicParameters = _privateRsa.ExportParameters(includePrivateParameters: false);
        using (var publicRsa = RSA.Create())
        {
            publicRsa.ImportParameters(publicParameters);
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(publicRsa));
            KeyId = Base64UrlEncoder.Encode(jwk.ComputeJwkThumbprint());
        }

        PrivateKey = new RsaSecurityKey(_privateRsa) { KeyId = KeyId };

        // Desliga o cache de SignatureProvider do Microsoft.IdentityModel.Tokens
        // para a chave (bug documentado da própria biblioteca: o cache
        // estático de CryptoProviderFactory.Default indexa por KeyId/algoritmo,
        // não por identidade do objeto RSA — dois RsaSecurityKey DIFERENTES que
        // computam o mesmo KeyId (ex.: dois hosts de teste carregando o mesmo
        // PEM) colidem no mesmo slot de cache, e a liberação de um provider
        // dispõe o RSA do outro por baixo dos panos, produzindo
        // ObjectDisposedException em uso legítimo e ainda vivo da chave). Sem
        // rotação de chave nesta etapa (nota técnica de BE-40), o custo de
        // recriar o SignatureProvider a cada chamada é desprezível perto do
        // risco de uma chave compartilhada ser derrubada por outro dono.
        PrivateKey.CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false };
    }

    public void Dispose()
    {
        _privateRsa.Dispose();
    }
}
