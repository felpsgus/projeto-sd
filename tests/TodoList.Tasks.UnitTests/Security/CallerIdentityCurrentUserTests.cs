using FluentAssertions;
using Microsoft.AspNetCore.Http;
using TodoList.Tasks.Api.Security;
using Xunit;

namespace TodoList.Tasks.UnitTests.Security;

/// <summary>
/// <see cref="CallerIdentityCurrentUser"/> (BE-35, D-34) — lê o dono da
/// tarefa da metadata gRPC <c>x-user-id</c>. O caso "header ausente/malformado"
/// é responsabilidade de <c>RequireCallerIdentityInterceptor</c>, executado
/// antes do RPC no pipeline real; aqui só se verifica o contrato desta classe
/// isoladamente.
/// </summary>
public class CallerIdentityCurrentUserTests
{
    [Fact]
    public void Id_ComHeaderValido_DevolveOGuid()
    {
        var userId = Guid.NewGuid();
        var currentUser = CreateWithHeader(userId.ToString());

        currentUser.Id.Should().Be(userId);
    }

    [Fact]
    public void IsAuthenticated_ComHeaderValido_EhVerdadeiro()
    {
        var currentUser = CreateWithHeader(Guid.NewGuid().ToString());

        currentUser.IsAuthenticated.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-um-guid")]
    public void IsAuthenticated_ComHeaderAusenteOuInvalido_EhFalso(string? headerValue)
    {
        var currentUser = CreateWithHeader(headerValue);

        currentUser.IsAuthenticated.Should().BeFalse();
    }

    [Fact] // Defensivo: nunca deveria acontecer na chamada real (o interceptor garante isso antes) — mas não pode virar um dono inventado.
    public void Id_ComHeaderAusente_Lanca()
    {
        var currentUser = CreateWithHeader(null);

        var act = () => currentUser.Id;

        act.Should().Throw<InvalidOperationException>();
    }

    private static CallerIdentityCurrentUser CreateWithHeader(string? value)
    {
        var context = new DefaultHttpContext();

        if (value is not null)
        {
            context.Request.Headers[CallerIdentityCurrentUser.HeaderName] = value;
        }

        var accessor = new HttpContextAccessor { HttpContext = context };

        return new CallerIdentityCurrentUser(accessor);
    }
}
