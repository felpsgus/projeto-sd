using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using TodoList.Identity.Api.Configuration;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// D-39/CA-21/CA-22 de BE-40: <c>Persisted</c> é o padrão de
/// <c>UserStore:Provider</c> em todo ambiente de deploy, e <c>InMemory</c>
/// fica restrito à suíte de testes.
/// </summary>
public partial class UserStoreProviderConfigurationTests
{
    [Fact] // CA-21
    public void AppsettingsDoIdentity_DeclaraUserStoreProviderComoPersisted()
    {
        var appsettingsPath = Path.Combine(
            SolutionPathHelper.SolutionRoot, "src", "Identity", "TodoList.Identity.Api", "appsettings.json");

        File.Exists(appsettingsPath).Should().BeTrue($"esperava encontrar {appsettingsPath}");

        using var document = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
        var provider = document.RootElement.GetProperty("UserStore").GetProperty("Provider").GetString();

        provider.Should().Be(
            UserStoreOptions.PersistedProvider,
            "D-39: dados de demonstração só do Postgres — InMemory não pode ser o padrão de nenhum appsettings.json de deploy");
    }

    /// <summary>
    /// CA-22 — varre os arquivos de configuração de deploy (compose e
    /// <c>*.env.example</c> da VM) e o <c>appsettings.json</c> de produção do
    /// Identity, procurando por <c>UserStore:Provider</c>/<c>UserStore__Provider</c>
    /// configurado como <c>InMemory</c>. A verificação é pelo VALOR
    /// configurado — JSON é parseado de verdade, .env/compose casam a
    /// variável <c>UserStore__Provider</c> por regex — não por uma varredura
    /// textual de "InMemory" em qualquer lugar do arquivo: um comentário
    /// explicando por que o padrão NÃO é InMemory (como em
    /// <c>deploy/identity.env.example</c>) não é uma violação de D-39, é
    /// documentação da própria decisão.
    /// </summary>
    [Fact] // CA-22
    public void ArquivosDeDeploy_NuncaConfiguramUserStoreProviderComoInMemory()
    {
        var solutionRoot = SolutionPathHelper.SolutionRoot;

        var appsettingsPath = Path.Combine(solutionRoot, "src", "Identity", "TodoList.Identity.Api", "appsettings.json");
        if (File.Exists(appsettingsPath))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
            var provider = document.RootElement.GetProperty("UserStore").GetProperty("Provider").GetString();

            provider.Should().NotBe(
                UserStoreOptions.InMemoryProvider,
                $"'{appsettingsPath}' configura UserStore:Provider=InMemory — D-39 exige Persisted em todo ambiente de deploy");
        }

        var arquivosDeVariaveis = new List<string> { Path.Combine(solutionRoot, "docker-compose.yml") };

        var deployDir = Path.Combine(solutionRoot, "deploy");
        if (Directory.Exists(deployDir))
        {
            arquivosDeVariaveis.AddRange(Directory.EnumerateFiles(deployDir, "*.env.example", SearchOption.TopDirectoryOnly));
        }

        arquivosDeVariaveis = arquivosDeVariaveis.Where(File.Exists).ToList();
        arquivosDeVariaveis.Should().NotBeEmpty();

        foreach (var arquivo in arquivosDeVariaveis)
        {
            var conteudo = File.ReadAllText(arquivo);

            foreach (Match match in UserStoreProviderValuePattern().Matches(conteudo))
            {
                var valor = match.Groups["valor"].Value.Trim().Trim('"');

                valor.Should().NotBe(
                    UserStoreOptions.InMemoryProvider,
                    $"'{arquivo}' configura UserStore__Provider=InMemory — D-39 exige Persisted em todo ambiente de deploy");
            }
        }
    }

    // Casa "UserStore__Provider=valor" (.env) e "UserStore__Provider: valor" (docker-compose.yml).
    [GeneratedRegex(@"UserStore__Provider\s*[:=]\s*(?<valor>[^\r\n,}]+)")]
    private static partial Regex UserStoreProviderValuePattern();
}
