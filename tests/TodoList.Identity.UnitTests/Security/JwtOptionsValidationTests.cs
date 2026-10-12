using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TodoList.Identity.Infrastructure.Security;
using Xunit;

namespace TodoList.Identity.UnitTests.Security;

/// <summary>
/// <see cref="JwtOptions"/> (BE-08, RS256 desde BE-40/D-38) — CA-03 (sem
/// <c>PrivateKeyPath</c>), CA-04 (arquivo inexistente), CA-05 (arquivo que
/// não é PEM PKCS8) e CA-06 (chave RSA menor que 2048 bits), no mesmo padrão
/// de <see cref="OptionsValidationTests"/>: sobe um <see cref="IHost"/>
/// mínimo com <c>ValidateOnStart</c> de verdade, não chama <c>Validate</c>
/// diretamente. Nenhuma chave de teste é versionada — geradas em memória
/// (<see cref="TestRsaKeyFile"/>) e gravadas num arquivo temporário só para o
/// teste que precisa de um caminho real.
/// </summary>
public class JwtOptionsValidationTests
{
    [Fact] // CA-03
    public async Task Host_SemNenhumaConfiguracaoDeJwt_FalhaAoIniciar()
    {
        var act = () => StartHostAsync(settings: []);

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact] // CA-03
    public async Task Host_SemIssuer_FalhaAoIniciarComMensagemNomeandoAChave()
    {
        using var chave = TestRsaKeyFile.Create();
        var settings = ValidSettings(chave.Path);
        settings.Remove($"{JwtOptions.SectionName}:{nameof(JwtOptions.Issuer)}");

        var act = () => StartHostAsync(settings);

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should().Contain("Jwt:Issuer");
    }

    [Fact] // CA-03
    public async Task Host_SemAudience_FalhaAoIniciarComMensagemNomeandoAChave()
    {
        using var chave = TestRsaKeyFile.Create();
        var settings = ValidSettings(chave.Path);
        settings.Remove($"{JwtOptions.SectionName}:{nameof(JwtOptions.Audience)}");

        var act = () => StartHostAsync(settings);

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should().Contain("Jwt:Audience");
    }

    [Fact] // CA-03 — PrivateKeyPath ausente
    public async Task Host_SemPrivateKeyPath_FalhaAoIniciarComMensagemNomeandoAChave()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{JwtOptions.SectionName}:{nameof(JwtOptions.Issuer)}"] = "todolist-identity",
            [$"{JwtOptions.SectionName}:{nameof(JwtOptions.Audience)}"] = "todolist",
        };

        var act = () => StartHostAsync(settings);

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should().Contain("Jwt:PrivateKeyPath");
    }

    [Fact] // CA-04 — arquivo inexistente
    public async Task Host_ComPrivateKeyPathApontandoParaArquivoInexistente_FalhaAoIniciar()
    {
        var caminhoInexistente = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"nao-existe-{Guid.NewGuid():N}.pem");
        var settings = ValidSettings(caminhoInexistente);

        var act = () => StartHostAsync(settings);

        var assertion = await act.Should().ThrowAsync<OptionsValidationException>();
        assertion.Which.Message.Should().Contain("Jwt:PrivateKeyPath");
        assertion.Which.Message.Should().NotContain(caminhoInexistente, "a mensagem nunca expõe o caminho resolvido");
    }

    [Fact] // CA-05 — arquivo existe, mas não é PEM PKCS8
    public async Task Host_ComPrivateKeyPathApontandoParaTextoArbitrario_FalhaAoIniciar()
    {
        var caminho = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"nao-e-um-pem-{Guid.NewGuid():N}.pem");
        await File.WriteAllTextAsync(caminho, "isto não é um PEM, é só texto qualquer");
        try
        {
            var settings = ValidSettings(caminho);

            var act = () => StartHostAsync(settings);

            var assertion = await act.Should().ThrowAsync<OptionsValidationException>();
            assertion.Which.Message.Should().Contain("Jwt:PrivateKeyPath");
            assertion.Which.Message.Should().NotContain("não é um PEM, é só texto qualquer", "a mensagem nunca expõe o conteúdo do arquivo");
        }
        finally
        {
            File.Delete(caminho);
        }
    }

    [Fact] // CA-06 — chave RSA de 1024 bits
    public async Task Host_ComChaveRsaDe1024Bits_FalhaAoIniciarComMensagemIndicandoOMinimo()
    {
        using var chave = TestRsaKeyFile.Create(keySizeInBits: 1024);
        var settings = ValidSettings(chave.Path);

        var act = () => StartHostAsync(settings);

        var assertion = await act.Should().ThrowAsync<OptionsValidationException>();
        assertion.Which.Message.Should().Contain("Jwt:PrivateKeyPath");
        assertion.Which.Message.Should().Contain("2048");
    }

    [Fact] // CA-06 — chave RSA de 2048 bits inicia sem erro
    public async Task Host_ComChaveRsaDe2048Bits_IniciaSemErro()
    {
        using var chave = TestRsaKeyFile.Create(keySizeInBits: 2048);
        var settings = ValidSettings(chave.Path);

        using var host = await StartHostAsync(settings);

        await host.StopAsync();
    }

    [Fact]
    public async Task Host_ComConfiguracaoValidaCompleta_IniciaSemErro()
    {
        using var chave = TestRsaKeyFile.Create();

        using var host = await StartHostAsync(ValidSettings(chave.Path));

        var options = host.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        options.Issuer.Should().Be("todolist-identity");
        options.Audience.Should().Be("todolist");
        options.AccessTokenMinutes.Should().Be(JwtOptions.DefaultAccessTokenMinutes);
        options.RefreshTokenDays.Should().Be(JwtOptions.DefaultRefreshTokenDays);

        await host.StopAsync();
    }

    private static Dictionary<string, string?> ValidSettings(string privateKeyPath) => new()
    {
        [$"{JwtOptions.SectionName}:{nameof(JwtOptions.Issuer)}"] = "todolist-identity",
        [$"{JwtOptions.SectionName}:{nameof(JwtOptions.Audience)}"] = "todolist",
        [$"{JwtOptions.SectionName}:{nameof(JwtOptions.PrivateKeyPath)}"] = privateKeyPath,
    };

    private static async Task<IHost> StartHostAsync(Dictionary<string, string?> settings)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.Sources.Clear();
                configuration.AddInMemoryCollection(settings);
            })
            .ConfigureServices((context, services) =>
            {
                services
                    .AddOptions<JwtOptions>()
                    .Bind(context.Configuration.GetSection(JwtOptions.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
            })
            .Build();

        await host.StartAsync();

        return host;
    }
}
