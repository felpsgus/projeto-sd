using FluentAssertions;
using Xunit;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>
/// BE-40, D-38 — varredura: nenhum arquivo de <c>src/Gateway/**</c> contém
/// <c>PrivateKey</c> (em qualquer caixa — o Gateway nunca tem motivo
/// legítimo para mencionar uma chave privada, só a pública) nem as formas de
/// configuração <c>Jwt:SigningKey</c>/<c>Jwt__SigningKey</c> (o desenho HS256
/// anterior, D-31). <c>Jwt:Issuer</c>, <c>Jwt:Audience</c> e
/// <c>Jwt:PublicKeyPath</c> continuam permitidas — são exatamente as três
/// chaves de configuração que o Gateway precisa (CA-17).
///
/// <para>
/// <b>Por que não bane a palavra "SigningKey" pura.</b> Diferente de
/// "PrivateKey", "SigningKey" aparece de forma legítima e inevitável em
/// <c>TokenValidationParameters.IssuerSigningKey</c>/
/// <c>ValidateIssuerSigningKey</c> — propriedades do framework usadas para
/// carregar a chave <b>pública</b> (nunca um segredo) no <c>AddJwtBearer</c>.
/// Banir a palavra toda geraria falso positivo nessa API sem cobrir nenhum
/// risco novo; as formas específicas de configuração
/// (<c>Jwt:SigningKey</c>/<c>Jwt__SigningKey</c>) continuam banidas.
/// </para>
/// </summary>
public class SecretScanTests
{
    private static readonly string[] _forbiddenTokens = ["Jwt:SigningKey", "Jwt__SigningKey", "PrivateKey"];
    private static readonly string[] _scannedExtensions = [".cs", ".json"];

    [Fact] // CA-17
    public void GatewaySourceTree_NaoContemNenhumaChaveDeAssinaturaDeJwt()
    {
        var gatewayRoot = FindGatewaySourceRoot();

        var files = Directory.EnumerateFiles(gatewayRoot, "*", SearchOption.AllDirectories)
            .Where(path => _scannedExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        files.Should().NotBeEmpty();

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);

            foreach (var token in _forbiddenTokens)
            {
                // NotContainEquivalentOf: comparação ignorando caixa (CA-17
                // exige banir "PrivateKey" "em qualquer caixa").
                content.Should().NotContainEquivalentOf(
                    token,
                    $"'{token}' não deve aparecer em {file} (D-38: a chave de assinatura — a metade privada — nunca sai do Identity)");
            }
        }
    }

    private static string FindGatewaySourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TodoList.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Não foi possível localizar a raiz do repositório (TodoList.sln).");
        }

        return Path.Combine(directory.FullName, "src", "Gateway");
    }
}
