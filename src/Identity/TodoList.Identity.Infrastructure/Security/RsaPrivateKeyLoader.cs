using System.Security.Cryptography;

namespace TodoList.Identity.Infrastructure.Security;

/// <summary>Motivo pelo qual <see cref="RsaPrivateKeyLoader.TryLoad"/> não conseguiu carregar a chave (BE-40).</summary>
public enum RsaPrivateKeyLoadFailure
{
    None,
    FileNotFound,
    Unreadable,
    InvalidPem,
}

/// <summary>
/// Ponto único de leitura/parse do PEM PKCS8 de chave privada RSA
/// (<see cref="JwtOptions.PrivateKeyPath"/>, BE-40/D-38). Usado tanto por
/// <see cref="JwtOptions.Validate"/> (na inicialização, para produzir a
/// mensagem de erro certa) quanto por <see cref="RsaSigningKeyProvider"/>
/// (para de fato carregar a chave usada em assinatura/validação) — evita
/// duas implementações de parse divergindo silenciosamente.
/// </summary>
internal static class RsaPrivateKeyLoader
{
    /// <summary>
    /// Tenta carregar <paramref name="path"/> como PEM PKCS8 de chave
    /// privada RSA. Nunca lança — o chamador decide o que fazer com a
    /// falha, e nenhuma mensagem construída a partir do retorno deve incluir
    /// o conteúdo do arquivo (só o nome da chave de configuração).
    /// </summary>
    public static (RSA? Rsa, RsaPrivateKeyLoadFailure Failure) TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return (null, RsaPrivateKeyLoadFailure.FileNotFound);
        }

        string pem;
        try
        {
            pem = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, RsaPrivateKeyLoadFailure.Unreadable);
        }

        // Exige especificamente PKCS8 ("BEGIN PRIVATE KEY") — não aceita
        // PKCS1 ("BEGIN RSA PRIVATE KEY") nem PKCS8 criptografado, como pede
        // a task (CA-05 de BE-40: qualquer coisa fora do formato esperado
        // falha, mesmo que fosse tecnicamente parseável por outro caminho).
        if (!pem.Contains("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal))
        {
            return (null, RsaPrivateKeyLoadFailure.InvalidPem);
        }

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            rsa.Dispose();
            return (null, RsaPrivateKeyLoadFailure.InvalidPem);
        }

        return (rsa, RsaPrivateKeyLoadFailure.None);
    }
}
