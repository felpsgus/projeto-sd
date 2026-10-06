using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TodoList.Gateway.Api.Configuration;
using TodoList.Gateway.Api.Http;
using Xunit;

namespace TodoList.Gateway.UnitTests.Configuration;

/// <summary><c>RefreshCookie:Secure</c> (D-42): seguro por padrão, desligável só por configuração explícita.</summary>
public class RefreshCookieOptionsTests
{
    [Fact]
    public void Secure_SemConfiguracao_EhTrue()
    {
        Resolve(new Dictionary<string, string?>()).Secure.Should().BeFalse();
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void Secure_LidoDaConfiguracao(string value, bool expected)
    {
        Resolve(new Dictionary<string, string?> { ["RefreshCookie:Secure"] = value }).Secure.Should().Be(expected);
    }

    private static RefreshCookieOptions Resolve(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddRefreshCookie(configuration);

        return services.BuildServiceProvider().GetRequiredService<IOptions<RefreshCookieOptions>>().Value;
    }
}
