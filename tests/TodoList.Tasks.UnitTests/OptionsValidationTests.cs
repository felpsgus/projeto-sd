using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TodoList.Tasks.Api.Configuration;
using TodoList.Tasks.Infrastructure.Identity;
using Xunit;

namespace TodoList.Tasks.UnitTests;

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
            [$"{ServiceOptions.SectionName}:{nameof(ServiceOptions.DisplayName)}"] = "Tasks Service",
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

/// <summary>
/// BE-30, CA-02 e CA-05 — mesma ideia de <see cref="OptionsValidationTests"/>
/// acima, agora para <see cref="IdentityGrpcOptions"/>: a seção
/// <c>Identity</c> ausente, com <c>GrpcAddress</c> vazio ou não parseável como
/// URI absoluta, ou com <c>GrpcTimeoutSeconds</c> zero/negativo, precisa
/// derrubar a inicialização — nunca esperar até a primeira chamada gRPC.
/// Chama <c>AddIdentityGrpcClient</c> de verdade (o mesmo método usado por
/// <c>Program.cs</c>), não uma reimplementação da validação no teste.
/// </summary>
public class IdentityGrpcOptionsValidationTests
{
    [Fact] // CA-02 — seção Identity inteira ausente
    public async Task Host_SemSecaoIdentityConfigurada_FalhaAoIniciarNomeandoAChave()
    {
        using var host = BuildHost(settings: new Dictionary<string, string?>());

        var act = async () => await host.StartAsync();

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .WithMessage("*Identity:GrpcAddress*");
    }

    [Theory] // CA-02 — presente mas não parseável como URI absoluta (não só "vazio")
    [InlineData("identity-service")] // sem esquema
    [InlineData("nem-uma-uri:// isto tem espaço")] // lixo mesmo
    public async Task Host_ComGrpcAddressNaoParseavelComoUriAbsoluta_FalhaAoIniciarNomeandoAChave(string enderecoInvalido)
    {
        using var host = BuildHost(settings: new Dictionary<string, string?>
        {
            ["Identity:GrpcAddress"] = enderecoInvalido,
        });

        var act = async () => await host.StartAsync();

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .WithMessage("*Identity:GrpcAddress*");
    }

    [Fact] // CA-02 — controle positivo: URI absoluta válida inicia sem erro
    public async Task Host_ComGrpcAddressUriAbsolutaValida_IniciaSemErro()
    {
        using var host = BuildHost(settings: new Dictionary<string, string?>
        {
            ["Identity:GrpcAddress"] = "http://localhost:5081",
        });

        var act = async () => await host.StartAsync();

        await act.Should().NotThrowAsync();

        await host.StopAsync();
    }

    [Theory] // CA-05 — zero ou negativo é rejeitado na inicialização
    [InlineData("0")]
    [InlineData("-1")]
    public async Task Host_ComGrpcTimeoutSecondsZeroOuNegativo_FalhaAoIniciar(string timeoutInvalido)
    {
        using var host = BuildHost(settings: new Dictionary<string, string?>
        {
            ["Identity:GrpcAddress"] = "http://localhost:5081",
            ["Identity:GrpcTimeoutSeconds"] = timeoutInvalido,
        });

        var act = async () => await host.StartAsync();

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .WithMessage("*Identity:GrpcTimeoutSeconds*");
    }

    private static IHost BuildHost(Dictionary<string, string?> settings) =>
        Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.Sources.Clear();
                configuration.AddInMemoryCollection(settings);
            })
            .ConfigureServices((context, services) => services.AddIdentityGrpcClient(context.Configuration))
            .Build();
}
