using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using TodoList.Gateway.Api.Backends;
using TodoList.Gateway.Api.ErrorHandling;
using Xunit;

namespace TodoList.Gateway.UnitTests.ErrorHandling;

public class GlobalExceptionHandlerTests
{
    /// <summary>
    /// Regressão: o navegador aborta a requisição (troca de página com chamada em voo), o gRPC de
    /// saída volta <see cref="StatusCode.Cancelled"/> e o Gateway registrava um 500 com log de erro.
    /// </summary>
    [Fact]
    public async Task RequisicaoAbortadaPeloCliente_Responde499SemTratarComoErroInterno()
    {
        var handler = new GlobalExceptionHandler(null!, null!, NullLogger<GlobalExceptionHandler>.Instance);
        var httpContext = new DefaultHttpContext { RequestAborted = new CancellationToken(canceled: true) };
        var exception = new BackendCallException(new RpcException(new Status(StatusCode.Cancelled, "Call canceled by the client.")));

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);
    }
}
