using FluentAssertions;
using Grpc.Core;
using TodoList.Tasks.Api.Grpc;
using TodoList.Tasks.Api.Security;
using Xunit;

namespace TodoList.Tasks.UnitTests.Grpc;

/// <summary>
/// <see cref="RequireCallerIdentityInterceptor"/> (BE-35, CA-08, CA-09) — a
/// chamada nunca alcança a continuação (o RPC real) sem uma metadata
/// <c>x-user-id</c> válida.
/// </summary>
public class RequireCallerIdentityInterceptorTests
{
    private readonly RequireCallerIdentityInterceptor _interceptor = new();

    [Fact] // CA-08 — metadata ausente
    public async Task UnaryServerHandler_SemMetadata_LancaUnauthenticatedSemChamarAContinuacao()
    {
        var context = new FakeServerCallContext();
        var continuacaoFoiChamada = false;

        Func<Task> act = () => _interceptor.UnaryServerHandler(
            "request",
            context,
            (_, _) =>
            {
                continuacaoFoiChamada = true;
                return Task.FromResult("response");
            });

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        continuacaoFoiChamada.Should().BeFalse();
    }

    [Theory] // CA-09 — presente mas não é um Guid
    [InlineData("")]
    [InlineData("nao-e-um-guid")]
    public async Task UnaryServerHandler_ComMetadataNaoGuid_LancaUnauthenticatedSemChamarAContinuacao(string valor)
    {
        var metadata = new Metadata { { CallerIdentityCurrentUser.HeaderName, valor } };
        var context = new FakeServerCallContext(metadata);
        var continuacaoFoiChamada = false;

        Func<Task> act = () => _interceptor.UnaryServerHandler(
            "request",
            context,
            (_, _) =>
            {
                continuacaoFoiChamada = true;
                return Task.FromResult("response");
            });

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        continuacaoFoiChamada.Should().BeFalse();
    }

    [Fact] // Controle positivo — metadata válida chama a continuação normalmente.
    public async Task UnaryServerHandler_ComMetadataGuidValido_ChamaAContinuacao()
    {
        var metadata = new Metadata { { CallerIdentityCurrentUser.HeaderName, Guid.NewGuid().ToString() } };
        var context = new FakeServerCallContext(metadata);
        var continuacaoFoiChamada = false;

        var resultado = await _interceptor.UnaryServerHandler(
            "request",
            context,
            (_, _) =>
            {
                continuacaoFoiChamada = true;
                return Task.FromResult("response");
            });

        continuacaoFoiChamada.Should().BeTrue();
        resultado.Should().Be("response");
    }
}
