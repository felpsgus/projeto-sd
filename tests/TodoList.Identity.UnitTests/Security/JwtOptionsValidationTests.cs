using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TodoList.Identity.Infrastructure.Security;
using Xunit;

namespace TodoList.Identity.UnitTests.Security;

/// <summary>
/// <see cref="JwtOptions"/> (BE-08) — CA-02 (issuer/audience/chave ausentes
/// falham o start) e CA-03 (chave menor que 32 bytes falha, 32 bytes passa),
/// no mesmo padrão de <see cref="OptionsValidationTests"/>: sobe um
/// <see cref="IHost"/> mínimo com <c>ValidateOnStart</c> de verdade, não
/// chama <c>Validate</c> diretamente.
/// </summary>
public class JwtOptionsValidationTests
{
    private const string ValidSigningKey = "01234567890123456789012345678901"; // 33 bytes ASCII

    [Fact] // CA-02
    public async Task Host_SemNenhumaChaveDeJwt_FalhaAoIniciar()
    {
        var act = () => StartHostAsync(settings: []);

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact] // CA-02
    public async Task Host_SemIssuer_FalhaAoIniciarComMensagemNomeandoAChave()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{JwtOptions.SectionName}:{nameof(JwtOptions.Audience)}"] = "todolist",
            [$"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)}"] = ValidSigningKey,
        };

        var act = () => StartHostAsync(settings);

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should().Contain("Jwt:Issuer");
    }

    [Fact] // CA-02
    public async Task Host_SemAudience_FalhaAoIniciarComMensagemNomeandoAChave()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{JwtOptions.SectionName}:{nameof(JwtOptions.Issuer)}"] = "todolist-identity",
            [$"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)}"] = ValidSigningKey,
        };

        var act = () => StartHostAsync(settings);

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should().Contain("Jwt:Audience");
    }

    [Fact] // CA-02
    public async Task Host_SemSigningKey_FalhaAoIniciarComMensagemNomeandoAChave()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{JwtOptions.SectionName}:{nameof(JwtOptions.Issuer)}"] = "todolist-identity",
            [$"{JwtOptions.SectionName}:{nameof(JwtOptions.Audience)}"] = "todolist",
        };

        var act = () => StartHostAsync(settings);

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should().Contain("Jwt:SigningKey");
    }

    [Fact] // CA-03 — mensagem nomeia a chave, nunca o valor recebido
    public async Task Host_ComSigningKeyMenorQue32Bytes_FalhaAoIniciar()
    {
        var chaveCurta = new string('a', 31);
        var settings = ValidSettings(signingKey: chaveCurta);

        var act = () => StartHostAsync(settings);

        var assertion = await act.Should().ThrowAsync<OptionsValidationException>();
        assertion.Which.Message.Should().Contain("Jwt:SigningKey");
        assertion.Which.Message.Should().NotContain(chaveCurta, "a mensagem de erro nunca expõe o valor da chave");
    }

    [Fact] // CA-03
    public async Task Host_ComSigningKeyDe32Bytes_IniciaSemErro()
    {
        var chaveMinima = new string('a', 32);
        var settings = ValidSettings(signingKey: chaveMinima);

        using var host = await StartHostAsync(settings);

        await host.StopAsync();
    }

    [Fact]
    public async Task Host_ComConfiguracaoValidaCompleta_IniciaSemErro()
    {
        using var host = await StartHostAsync(ValidSettings(ValidSigningKey));

        var options = host.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        options.Issuer.Should().Be("todolist-identity");
        options.Audience.Should().Be("todolist");
        options.AccessTokenMinutes.Should().Be(JwtOptions.DefaultAccessTokenMinutes);
        options.RefreshTokenDays.Should().Be(JwtOptions.DefaultRefreshTokenDays);

        await host.StopAsync();
    }

    private static Dictionary<string, string?> ValidSettings(string signingKey) => new()
    {
        [$"{JwtOptions.SectionName}:{nameof(JwtOptions.Issuer)}"] = "todolist-identity",
        [$"{JwtOptions.SectionName}:{nameof(JwtOptions.Audience)}"] = "todolist",
        [$"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)}"] = signingKey,
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
