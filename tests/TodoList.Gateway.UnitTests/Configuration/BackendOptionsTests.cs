using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TodoList.Gateway.Api.Backends;
using TodoList.Gateway.Api.Configuration;
using Xunit;

namespace TodoList.Gateway.UnitTests.Configuration;

/// <summary>BE-36, CA-22/CA-23 — endereços dos backends são lidos de configuração e validados no start.</summary>
public class BackendOptionsTests
{
    [Fact] // CA-22 — trocar o endereço na configuração muda o valor resolvido, sem recompilar
    public void AddBackendGrpcClients_LeEnderecosDaConfiguracao_SemValorLiteralNoCodigo()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Backends:IdentityGrpcAddress"] = "http://outro-host:9999",
            ["Backends:TasksGrpcAddress"] = "http://outro-host:8888",
        });

        var services = new ServiceCollection();
        services.AddBackendGrpcClients(configuration);
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<BackendOptions>>().Value;

        options.IdentityGrpcAddress.Should().Be("http://outro-host:9999");
        options.TasksGrpcAddress.Should().Be("http://outro-host:8888");
    }

    [Fact] // CA-23 — endereço ausente falha no start (ValidateOnStart), não na primeira requisição
    public void AddBackendGrpcClients_SemIdentityGrpcAddress_FalhaNoStart()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Backends:TasksGrpcAddress"] = "http://localhost:5101",
        });

        var services = new ServiceCollection();
        services.AddBackendGrpcClients(configuration);
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<BackendOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact] // CA-23 — endereço presente mas não é URI absoluta também falha no start
    public void AddBackendGrpcClients_ComEnderecoQueNaoEUriAbsoluta_FalhaNoStart()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Backends:IdentityGrpcAddress"] = "identity-service",
            ["Backends:TasksGrpcAddress"] = "http://localhost:5101",
        });

        var services = new ServiceCollection();
        services.AddBackendGrpcClients(configuration);
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<BackendOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
