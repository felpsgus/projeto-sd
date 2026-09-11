using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TodoList.Identity.Api.Configuration;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// <see cref="UserStoreOptions"/> (BE-33) — CA-07: <c>DemoUserPassword</c>
/// ausente com <c>SeedDemoUsers=true</c> falha a inicialização, mesmo padrão
/// de <see cref="JwtOptionsValidationTests"/>: sobe um <see cref="IHost"/>
/// mínimo com <c>ValidateOnStart</c> de verdade.
/// </summary>
public class UserStoreOptionsValidationTests
{
    [Fact] // CA-07
    public async Task Host_ComSeedDemoUsersLigadoESemDemoUserPassword_FalhaAoIniciar()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{UserStoreOptions.SectionName}:{nameof(UserStoreOptions.Provider)}"] = UserStoreOptions.PersistedProvider,
            [$"{UserStoreOptions.SectionName}:{nameof(UserStoreOptions.SeedDemoUsers)}"] = "true",
        };

        var act = () => StartHostAsync(settings);

        var assertion = await act.Should().ThrowAsync<OptionsValidationException>();
        assertion.Which.Message.Should().Contain("UserStore:DemoUserPassword");
    }

    [Fact] // CA-07 — presente, inicia sem erro
    public async Task Host_ComSeedDemoUsersLigadoEDemoUserPasswordPresente_IniciaSemErro()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{UserStoreOptions.SectionName}:{nameof(UserStoreOptions.Provider)}"] = UserStoreOptions.PersistedProvider,
            [$"{UserStoreOptions.SectionName}:{nameof(UserStoreOptions.SeedDemoUsers)}"] = "true",
            [$"{UserStoreOptions.SectionName}:{nameof(UserStoreOptions.DemoUserPassword)}"] = "senha-de-demonstracao",
        };

        using var host = await StartHostAsync(settings);

        await host.StopAsync();
    }

    [Fact] // SeedDemoUsers desligado (padrão) não exige DemoUserPassword
    public async Task Host_ComSeedDemoUsersDesligado_NaoExigeDemoUserPassword()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{UserStoreOptions.SectionName}:{nameof(UserStoreOptions.Provider)}"] = UserStoreOptions.InMemoryProvider,
        };

        using var host = await StartHostAsync(settings);

        await host.StopAsync();
    }

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
                    .AddOptions<UserStoreOptions>()
                    .Bind(context.Configuration.GetSection(UserStoreOptions.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
            })
            .Build();

        await host.StartAsync();

        return host;
    }
}
