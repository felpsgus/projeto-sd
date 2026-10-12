using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TodoList.Identity.Infrastructure.Security;
using Xunit;

namespace TodoList.Identity.UnitTests.Security;

/// <summary>
/// BE-40: um <c>Jwt:PrivateKeyPath</c> relativo é resolvido contra o
/// <c>ContentRootPath</c> do host (<see cref="IHostEnvironment"/>), não
/// contra o diretório corrente do processo — o <c>PostConfigure&lt;JwtOptions&gt;</c>
/// registrado em <see cref="ServiceCollectionExtensions.AddIdentitySecurity"/>.
/// Um caminho absoluto (como os demais testes de <see cref="JwtOptions"/>
/// usam) passa por esse mesmo PostConfigure sem ser alterado.
/// </summary>
public class JwtPrivateKeyPathResolutionTests
{
    [Fact]
    public async Task PrivateKeyPathRelativo_EResolvidoContraOContentRootPathDoHost_NaoContraODiretorioCorrente()
    {
        using var pastaDoContentRoot = new TemporaryDirectory();
        using var chave = TestRsaKeyFile.Create();

        // A chave física fica em outro lugar (TestRsaKeyFile usa Path.GetTempPath()),
        // então referenciamos com um caminho relativo cujo alvo real só existe
        // quando resolvido contra pastaDoContentRoot — nunca contra o diretório
        // corrente do processo de teste (que não é pastaDoContentRoot).
        var nomeDoArquivo = System.IO.Path.GetFileName(chave.Path);
        var caminhoRelativoDentroDoContentRoot = System.IO.Path.Combine(pastaDoContentRoot.Path, nomeDoArquivo);
        File.Copy(chave.Path, caminhoRelativoDentroDoContentRoot);

        var caminhoRelativo = nomeDoArquivo;
        var environment = new FakeHostEnvironment(pastaDoContentRoot.Path);

        var settings = new Dictionary<string, string?>
        {
            [$"{JwtOptions.SectionName}:{nameof(JwtOptions.Issuer)}"] = "todolist-identity",
            [$"{JwtOptions.SectionName}:{nameof(JwtOptions.Audience)}"] = "todolist",
            [$"{JwtOptions.SectionName}:{nameof(JwtOptions.PrivateKeyPath)}"] = caminhoRelativo,
            [$"{PasswordHashingOptions.SectionName}:{nameof(PasswordHashingOptions.Iterations)}"] = "1000",
        };

        using var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.Sources.Clear();
                configuration.AddInMemoryCollection(settings);
            })
            .ConfigureServices((context, services) =>
                services.AddIdentitySecurity(context.Configuration, environment))
            .Build();

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        options.PrivateKeyPath.Should().Be(caminhoRelativoDentroDoContentRoot);

        await host.StopAsync();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"jwt-test-root-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string contentRootPath) => ContentRootPath = contentRootPath;

        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "TodoList.Identity.UnitTests";

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
