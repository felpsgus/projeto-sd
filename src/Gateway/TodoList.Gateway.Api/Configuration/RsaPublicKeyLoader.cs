using System.Security.Cryptography;

namespace TodoList.Gateway.Api.Configuration;

/// <summary>Motivo pelo qual <see cref="RsaPublicKeyLoader.TryLoad"/> não conseguiu carregar a chave (BE-40).</summary>
public enum RsaPublicKeyLoadFailure
{
    None,
    FileNotFound,
    Unreadable,
    InvalidPem,
}

/// <summary>
/// Ponto único de leitura/parse do PEM SubjectPublicKeyInfo de chave pública
/// RSA (<see cref="JwtOptions.PublicKeyPath"/>, BE-40/D-38). Usado tanto por
/// <see cref="JwtOptions.Validate"/> (na inicialização, para produzir a
/// mensagem de erro certa) quanto por
/// <see cref="Authentication.JwtPublicKeyProvider"/> (para de fato carregar a
/// chave usada em <c>AddJwtBearer</c>) — evita duas implementações de parse
/// divergindo silenciosamente. Espelha o carregador equivalente do Identity
/// (que trata a outra metade da chave), do lado que só lê a parte pública.
/// </summary>
internal static class RsaPublicKeyLoader
{
    /// <summary>
    /// Tenta carregar <paramref name="path"/> como PEM SubjectPublicKeyInfo
    /// de chave pública RSA. Nunca lança — o chamador decide o que fazer com
    /// a falha, e nenhuma mensagem construída a partir do retorno deve
    /// incluir o conteúdo do arquivo (só o nome da chave de configuração).
    /// </summary>
    public static (RSA? Rsa, RsaPublicKeyLoadFailure Failure) TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return (null, RsaPublicKeyLoadFailure.FileNotFound);
        }

        string pem;
        try
        {
            pem = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, RsaPublicKeyLoadFailure.Unreadable);
        }

        // Exige especificamente SubjectPublicKeyInfo ("BEGIN PUBLIC KEY") —
        // mesmo espírito do carregador equivalente do Identity, que exige
        // PKCS8 do outro lado da chave: qualquer coisa fora do formato
        // esperado falha, mesmo que fosse tecnicamente parseável por outro
        // caminho.
        if (!pem.Contains("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal))
        {
            return (null, RsaPublicKeyLoadFailure.InvalidPem);
        }

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            rsa.Dispose();
            return (null, RsaPublicKeyLoadFailure.InvalidPem);
        }

        return (rsa, RsaPublicKeyLoadFailure.None);
    }
}
