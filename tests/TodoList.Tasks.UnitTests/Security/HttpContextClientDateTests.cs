using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;
using TodoList.Tasks.Api.Security;
using Xunit;

namespace TodoList.Tasks.UnitTests.Security;

/// <summary>
/// <see cref="HttpContextClientDate"/> (BE-13, D-18) — <c>X-Client-Date</c>
/// com fallback silencioso para a data UTC do <see cref="TimeProvider"/>.
/// </summary>
public class HttpContextClientDateTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 8, 21, 0, 30, 0, TimeSpan.Zero));

    [Fact] // CA-12 de BE-13
    public void Today_ComHeaderValido_DevolveADataDoHeader()
    {
        var clientDate = CreateWithHeader("2026-08-20");

        clientDate.Today.Should().Be(new DateOnly(2026, 8, 20));
    }

    [Fact] // CA-13 de BE-13
    public void Today_SemHeader_DevolveADataUtcDoTimeProvider()
    {
        var clientDate = CreateWithHeader(null);

        clientDate.Today.Should().Be(new DateOnly(2026, 8, 21));
    }

    [Theory] // CA-14 de BE-13
    [InlineData("ontem")]
    [InlineData("21/08/2026")]
    [InlineData("2026-13-45")]
    [InlineData("")]
    public void Today_ComHeaderMalformado_CaiNoFallbackSemErro(string headerValue)
    {
        var clientDate = CreateWithHeader(headerValue);

        clientDate.Today.Should().Be(new DateOnly(2026, 8, 21));
    }

    private HttpContextClientDate CreateWithHeader(string? value)
    {
        var context = new DefaultHttpContext();

        if (value is not null)
        {
            context.Request.Headers[HttpContextClientDate.HeaderName] = value;
        }

        var accessor = new HttpContextAccessor { HttpContext = context };

        return new HttpContextClientDate(accessor, _timeProvider);
    }
}
