using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TodoList.Identity.Api.Configuration;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// CA-08 — subir o serviço com uma seção de configuração obrigatória ausente
/// deve falhar na inicialização (ValidateOnStart), não na primeira requisição.
/// </summary>
public class OptionsValidationTests
{
    [Fact]
    public async Task Host_SemSecaoServiceConfigurada_FalhaAoIniciar()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, configuration) => configuration.Sources.Clear())
            .ConfigureServices((context, services) =>
            {
                services
                    .AddOptions<ServiceOptions>()
                    .Bind(context.Configuration.GetSection(ServiceOptions.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
            })
            .Build();

        var act = async () => await host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact]
    public async Task Host_ComSecaoServiceValida_IniciaSemErro()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{ServiceOptions.SectionName}:{nameof(ServiceOptions.DisplayName)}"] = "Identity Service",
        };

        using var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings))
            .ConfigureServices((context, services) =>
            {
                services
                    .AddOptions<ServiceOptions>()
                    .Bind(context.Configuration.GetSection(ServiceOptions.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
            })
            .Build();

        var act = async () => await host.StartAsync();

        await act.Should().NotThrowAsync();

        await host.StopAsync();
    }
}
