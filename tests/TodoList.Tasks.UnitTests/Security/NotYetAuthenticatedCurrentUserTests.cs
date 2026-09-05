using FluentAssertions;
using TodoList.Tasks.Api.Security;
using Xunit;

namespace TodoList.Tasks.UnitTests.Security;

/// <summary>
/// <see cref="NotYetAuthenticatedCurrentUser"/> — implementação registrada
/// quando <c>Tasks:AllowAnonymousCreate=false</c> (modo definitivo). Nunca
/// resolve uma identidade: é o que impede o serviço de aceitar qualquer
/// chamador silenciosamente enquanto BE-13 não existe no Tasks Service.
/// </summary>
public class NotYetAuthenticatedCurrentUserTests
{
    [Fact]
    public void IsAuthenticated_EhSempreFalso()
    {
        var currentUser = new NotYetAuthenticatedCurrentUser();

        currentUser.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void Id_Lanca_NuncaDevolveUmDonoInventado()
    {
        var currentUser = new NotYetAuthenticatedCurrentUser();

        var act = () => currentUser.Id;

        act.Should().Throw<InvalidOperationException>();
    }
}
