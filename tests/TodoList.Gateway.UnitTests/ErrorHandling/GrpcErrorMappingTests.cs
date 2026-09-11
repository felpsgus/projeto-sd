using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using TodoList.Gateway.Api.ErrorHandling;
using Xunit;

namespace TodoList.Gateway.UnitTests.ErrorHandling;

/// <summary>BE-36, D-35, CA-16 a CA-19 — cobertura de todo <see cref="StatusCode"/> da tabela de mapeamento.</summary>
public class GrpcErrorMappingTests
{
    [Fact]
    public async Task ToHttpResult_InvalidArgument_Vira400ComErrosPorCampo() // CA-06 (espelho D-35)
    {
        var validationErrorsJson = """{"Title":["obrigatório"]}""";

        var result = GrpcErrorMapping.ToHttpResult(StatusCode.InvalidArgument, "validation.failed", validationErrorsJson, "Requisição inválida.");

        var httpContext = new DefaultHttpContext { RequestServices = TestServices.Build() };
        httpContext.Response.Body = new MemoryStream();
        await result.ExecuteAsync(httpContext);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task ToHttpResult_NotFound_Vira404() // CA-16
    {
        var result = GrpcErrorMapping.ToHttpResult(StatusCode.NotFound, "owner.not_found", null, "não encontrado");

        var httpContext = await ExecuteAsync(result);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task ToHttpResult_FailedPrecondition_Vira409() // CA-17
    {
        var result = GrpcErrorMapping.ToHttpResult(StatusCode.FailedPrecondition, "owner_inactive", null, "dono inativo");

        var httpContext = await ExecuteAsync(result);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task ToHttpResult_Unauthenticated_Vira401()
    {
        var result = GrpcErrorMapping.ToHttpResult(StatusCode.Unauthenticated, null, null, "não autenticado");

        var httpContext = await ExecuteAsync(result);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    public async Task ToHttpResult_UnavailableOuDeadlineExceeded_Vira503ComRetryAfter(StatusCode statusCode) // CA-18
    {
        var result = GrpcErrorMapping.ToHttpResult(statusCode, null, null, "indisponível");

        var httpContext = await ExecuteAsync(result);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        httpContext.Response.Headers.RetryAfter.ToString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ToHttpResult_Internal_Vira500GenericoSemDetalheDeTransporte() // CA-19
    {
        const string mensagemCrua = "mensagem crua interna do RpcException, endereco-interno:5081";
        var result = GrpcErrorMapping.ToHttpResult(StatusCode.Internal, null, null, mensagemCrua);

        var httpContext = await ExecuteAsync(result);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();
        body.Should().NotContain(mensagemCrua, "CA-19: a mensagem crua de transporte nunca deve vazar no corpo do 500");
    }

    private static async Task<DefaultHttpContext> ExecuteAsync(IResult result)
    {
        var httpContext = new DefaultHttpContext { RequestServices = TestServices.Build() };
        httpContext.Response.Body = new MemoryStream();
        await result.ExecuteAsync(httpContext);

        return httpContext;
    }
}

/// <summary>Container de DI mínimo para executar um <see cref="IResult"/> fora de um host ASP.NET Core completo.</summary>
internal static class TestServices
{
    public static IServiceProvider Build() =>
        new ServiceCollection().AddLogging().BuildServiceProvider();
}
