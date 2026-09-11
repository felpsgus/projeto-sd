using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using TodoList.Identity.Application.Security;

namespace TodoList.Identity.Infrastructure.Security;

/// <summary>
/// Implementação de <see cref="IPasswordHasher"/> (BE-06) com PBKDF2-HMAC-SHA256
/// do BCL (<see cref="Rfc2898DeriveBytes"/>), sem dependência de pacote novo.
/// Formato de hash auto-descritivo — <c>pbkdf2-sha256$&lt;iterações&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;</c> —
/// para poder evoluir o custo (<see cref="PasswordHashingOptions"/>) sem
/// invalidar hashes já persistidos (CA-05). Sem estado e thread-safe —
/// registrado como singleton (ver <see cref="ServiceCollectionExtensions"/>).
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int SaltSizeInBytes = 16;
    private const int HashSizeInBytes = 32;

    private readonly IOptions<PasswordHashingOptions> _options;

    public Pbkdf2PasswordHasher(IOptions<PasswordHashingOptions> options)
    {
        _options = options;
    }

    public string Hash(string plainPassword)
    {
        var iterations = _options.Value.Iterations;
        var salt = RandomNumberGenerator.GetBytes(SaltSizeInBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(plainPassword, salt, iterations, HashAlgorithmName.SHA256, HashSizeInBytes);

        return $"{Prefix}${iterations.ToString(CultureInfo.InvariantCulture)}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string plainPassword, string hash)
    {
        // CA-03: qualquer formato inesperado — segmentos faltando, prefixo
        // desconhecido, Base64 inválido, iterações não numéricas ou ≤ 0 —
        // resulta em false, nunca exceção. Isso cobre inclusive um hash
        // legado que não siga este formato (ex.: o placeholder do T1 que o
        // DemoUserSeeder sincroniza para um hash real, BE-33 CA-09).
        var parts = hash.Split('$');

        if (parts.Length != 4 || parts[0] != Prefix)
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedHash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        // BE-08 (correção de revisão da BE-06, CA-03): salt ou hash vazios
        // (ex.: "pbkdf2-sha256$1000$AAAA$") decodificam sem erro, mas um
        // tamanho de saída 0 faz Rfc2898DeriveBytes.Pbkdf2 lançar
        // ArgumentOutOfRangeException. Hash malformado é "false", nunca
        // exceção — trata os dois como malformados antes de derivar.
        if (salt.Length == 0 || expectedHash.Length == 0)
        {
            return false;
        }

        // Iterações lidas do próprio hash armazenado, não da configuração
        // atual — é o que faz CA-05 funcionar: mudar PasswordHashingOptions
        // não invalida hashes antigos.
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(plainPassword, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
