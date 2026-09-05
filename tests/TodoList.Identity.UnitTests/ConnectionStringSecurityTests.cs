using FluentAssertions;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// CA-11 de BE-02: nenhuma connection string real fica versionada. Varre o
/// repositório inteiro (os dois serviços), não só o Identity — é um teste
/// repositório-wide, não faz sentido duplicá-lo no Tasks.
/// </summary>
public class ConnectionStringSecurityTests
{
    [Fact] // CA-11
    public void NenhumAppsettings_DeclaraSecaoConnectionStrings()
    {
        var srcRoot = Path.Combine(SolutionPathHelper.SolutionRoot, "src");

        // appsettings*.json nunca deve ter "ConnectionStrings" — nem em dev:
        // o valor só existe via dotnet user-secrets (fora do repo) ou
        // variável de ambiente (CI). Ver README.
        var arquivosComSecao = Directory.EnumerateFiles(srcRoot, "appsettings*.json", SearchOption.AllDirectories)
            .Where(arquivo => File.ReadAllText(arquivo).Contains("ConnectionStrings", StringComparison.OrdinalIgnoreCase))
            .ToList();

        arquivosComSecao.Should().BeEmpty(
            "connection string nunca deve ser versionada (CA-11) — configure via 'dotnet user-secrets' (dev) "
            + "ou variável de ambiente ConnectionStrings__<Nome> (CI), nunca em appsettings*.json");
    }

    // A única credencial literal tolerada em src/ é a do docker-compose.yml
    // local, usada como fallback pelas fábricas IDesignTimeDbContextFactory
    // (rodadas só pelo `dotnet ef`, nunca em runtime). Está repetida aqui de
    // propósito: se alguém mudar o fallback das fábricas, este teste quebra e
    // obriga a decisão a passar por uma revisão.
    private const string FallbackLocalTolerado =
        "Host=127.0.0.1;Port=5432;Database=todolist;Username=postgres;Password=postgres";

    [Fact] // CA-11
    public void NenhumCodigoFonte_ContemCredencialDeBancoAlemDoFallbackLocalDocumentado()
    {
        var srcRoot = Path.Combine(SolutionPathHelper.SolutionRoot, "src");

        // A isenção é do *valor*, não do diretório. Isentar Persistence/Design
        // inteiro deixaria uma credencial real passar justamente onde ninguém
        // olha; assim, qualquer "Password=" que não seja exatamente o fallback
        // documentado reprova, esteja no arquivo que estiver.
        var violacoes = Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(arquivo => !arquivo.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(arquivo => !arquivo.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(arquivo => File.ReadAllText(arquivo)
                .Replace(FallbackLocalTolerado, string.Empty, StringComparison.Ordinal)
                .Contains("Password=", StringComparison.OrdinalIgnoreCase))
            .ToList();

        violacoes.Should().BeEmpty(
            "a única credencial literal tolerada em src/ é o fallback local documentado das fábricas de "
            + "design-time — qualquer outra é uma connection string versionada (CA-11)");
    }
}
