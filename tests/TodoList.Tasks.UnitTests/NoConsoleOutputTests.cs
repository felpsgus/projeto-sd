using FluentAssertions;
using Xunit;

namespace TodoList.Tasks.UnitTests;

/// <summary>BE-24, CA-05 — o código de produção só escreve log pelo <c>ILogger</c> (Serilog JSON); nunca em <c>Console</c>.</summary>
public class NoConsoleOutputTests
{
    [Fact] // CA-05
    public void CodigoDeProducao_NaoUsaConsoleWriteNemWriteLine()
    {
        var src = Path.Combine(SolutionPathHelper.SolutionRoot, "src");

        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("Console.Write", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(src, file))
            .ToList();

        offenders.Should().BeEmpty("log estruturado vai por ILogger, não por Console.Write/WriteLine (CONVENCOES-CODIGO.md, 2.1)");
    }
}
