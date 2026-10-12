using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TodoList.Tasks.Application.Tasks;
using Xunit;

namespace TodoList.Tasks.UnitTests;

/// <summary>
/// BE-41 (D-09) — <see cref="PagingOptions"/> segue o mesmo padrão de
/// <see cref="OptionsValidationTests"/>: um valor inválido derruba a
/// inicialização (<c>ValidateOnStart</c>), não a primeira chamada de
/// <c>ListTasksHandler</c>. Sem seção <c>Paging</c> nenhuma configurada, os
/// defaults (20/100) se aplicam sem erro — a seção inteira é opcional.
/// </summary>
public class PagingOptionsValidationTests
{
    [Fact] // Sem a seção Paging configurada, os defaults (20/100) valem e o host inicia normalmente.
    public async Task Host_SemSecaoPagingConfigurada_IniciaComOsDefaults()
    {
        using var host = BuildHost(settings: new Dictionary<string, string?>());

        var act = async () => await host.StartAsync();

        await act.Should().NotThrowAsync();

        var options = host.Services.GetRequiredService<IOptions<PagingOptions>>().Value;
        options.DefaultPageSize.Should().Be(20);
        options.MaxPageSize.Should().Be(100);

        await host.StopAsync();
    }

    [Theory] // DefaultPageSize/MaxPageSize precisam ser positivos (D-09) — zero ou negativo derruba a inicialização.
    [InlineData("Paging:DefaultPageSize", "0")]
    [InlineData("Paging:DefaultPageSize", "-1")]
    [InlineData("Paging:MaxPageSize", "0")]
    [InlineData("Paging:MaxPageSize", "-1")]
    public async Task Host_ComValorNaoPositivo_FalhaAoIniciar(string chave, string valorInvalido)
    {
        using var host = BuildHost(settings: new Dictionary<string, string?> { [chave] = valorInvalido });

        var act = async () => await host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact] // D-09 — alterar o padrão via configuração muda o comportamento sem mudança de código.
    public async Task Host_ComPagingConfiguradoExplicitamente_UsaOsValoresConfigurados()
    {
        using var host = BuildHost(settings: new Dictionary<string, string?>
        {
            ["Paging:DefaultPageSize"] = "5",
            ["Paging:MaxPageSize"] = "50",
        });

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<PagingOptions>>().Value;
        options.DefaultPageSize.Should().Be(5);
        options.MaxPageSize.Should().Be(50);

        await host.StopAsync();
    }

    private static IHost BuildHost(Dictionary<string, string?> settings) =>
        Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.Sources.Clear();
                configuration.AddInMemoryCollection(settings);
            })
            .ConfigureServices((context, services) => services
                .AddOptions<PagingOptions>()
                .Bind(context.Configuration.GetSection(PagingOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart())
            .Build();
}
