using FluentAssertions;
using Xunit;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>
/// BE-36, CA-15/D-31 — varredura: nenhuma chave <c>Jwt:*</c>/<c>Jwt__*</c> nem
/// <c>SigningKey</c> existe em <c>src/Gateway/**</c> — a chave de assinatura
/// do JWT nunca sai do Identity.
/// </summary>
public class SecretScanTests
{
    private static readonly string[] _forbiddenTokens = ["Jwt:", "Jwt__", "SigningKey"];
    private static readonly string[] _scannedExtensions = [".cs", ".json"];

    [Fact] // CA-15
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
                content.Should().NotContain(token, $"'{token}' não deve aparecer em {file} (D-31: a chave de assinatura nunca sai do Identity)");
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
