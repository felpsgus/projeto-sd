namespace TodoList.Identity.UnitTests;

/// <summary>
/// Localiza a raiz do repositório (onde está <c>TodoList.sln</c>) a partir do
/// diretório de saída do teste, para permitir que os testes de arquitetura
/// leiam os <c>.csproj</c> diretamente, independentemente de onde `dotnet test`
/// é executado.
/// </summary>
internal static class SolutionPathHelper
{
    public static string SolutionRoot { get; } = FindSolutionRoot();

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TodoList.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Não foi possível localizar TodoList.sln a partir do diretório de teste.");
    }
}
