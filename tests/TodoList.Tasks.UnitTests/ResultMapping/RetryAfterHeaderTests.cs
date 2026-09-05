using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TodoList.SharedKernel;
using TodoList.Tasks.Api.ResultMapping;
using Xunit;

namespace TodoList.Tasks.UnitTests.ResultMapping;

/// <summary>
/// BE-28: todo <c>503</c> produzido por <c>ResultHttpResults</c> (ErrorType
/// <see cref="ErrorType.Unavailable"/>) traz o cabeçalho <c>Retry-After</c> —
/// nenhum outro <see cref="ErrorType"/> leva esse cabeçalho.
/// </summary>
public class RetryAfterHeaderTests
{
    [Fact]
    public async Task ToHttpResult_ComErrorTypeUnavailable_AdicionaCabecalhoRetryAfter()
    {
        var result = Result.Failure<string>(new Error("identity.unavailable", "Indisponível.", ErrorType.Unavailable));
        var httpContext = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };

        await result.ToHttpResult().ExecuteAsync(httpContext);

        httpContext.Response.Headers.RetryAfter.ToString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ToHttpResult_ComOutroErrorType_NaoAdicionaRetryAfter()
    {
        var result = Result.Failure<string>(new Error("task.owner_not_found", "Não encontrado.", ErrorType.NotFound));
        var httpContext = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };

        await result.ToHttpResult().ExecuteAsync(httpContext);

        httpContext.Response.Headers.RetryAfter.ToString().Should().BeEmpty();
    }
}
