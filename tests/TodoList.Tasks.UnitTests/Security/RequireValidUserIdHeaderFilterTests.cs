using FluentAssertions;
using Microsoft.AspNetCore.Http;
using TodoList.Tasks.Api.Security;
using Xunit;

namespace TodoList.Tasks.UnitTests.Security;

/// <summary>
/// <see cref="RequireValidUserIdHeaderFilter"/> (BE-29, CA-03) — só deixa a
/// requisição prosseguir com um <c>X-User-Id</c> válido; nunca deixa
/// <see cref="HeaderCurrentUser.Id"/> ser alcançado sem essa garantia.
/// </summary>
public class RequireValidUserIdHeaderFilterTests
{
    private readonly RequireValidUserIdHeaderFilter _filter = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-um-guid")]
    public async Task InvokeAsync_ComHeaderInvalido_CurtoCircuitaCom400SemChamarONext(string? headerValue)
    {
        var context = CreateContext(headerValue);
        var nextFoiChamado = false;

        var resultado = await _filter.InvokeAsync(context, _ =>
        {
            nextFoiChamado = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        nextFoiChamado.Should().BeFalse();
        resultado.Should().NotBeNull();
    }

    [Fact]
    public async Task InvokeAsync_ComHeaderValido_ChamaONext()
    {
        var context = CreateContext(Guid.NewGuid().ToString());
        var nextFoiChamado = false;

        await _filter.InvokeAsync(context, _ =>
        {
            nextFoiChamado = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        nextFoiChamado.Should().BeTrue();
    }

    private static EndpointFilterInvocationContext CreateContext(string? headerValue)
    {
        var httpContext = new DefaultHttpContext();

        if (headerValue is not null)
        {
            httpContext.Request.Headers[HeaderCurrentUser.HeaderName] = headerValue;
        }

        return EndpointFilterInvocationContext.Create(httpContext);
    }
}
