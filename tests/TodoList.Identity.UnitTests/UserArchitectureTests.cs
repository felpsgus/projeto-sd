using System.Reflection;
using FluentAssertions;
using TodoList.Identity.Domain.Users;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// Reflection sobre <see cref="User"/> (BE-04) — CA-07 (nenhum setter público
/// em nenhuma propriedade) e CA-08 (<see cref="User.Email"/> não tem setter
/// nenhum, nem privado — a imutabilidade é estrutural, RN-USER-03).
/// </summary>
public class UserArchitectureTests
{
    [Fact] // CA-07
    public void User_NaoExpoeNenhumSetterPublicoEmNenhumaPropriedade()
    {
        var properties = typeof(User).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        properties.Should().NotBeEmpty();
        properties.Should().OnlyContain(
            property => property.GetSetMethod(nonPublic: false) == null,
            "User não pode expor nenhum setter público (CA-07) — alterações só ocorrem via método de domínio");
    }

    [Fact] // CA-08
    public void User_Email_NaoTemNenhumSetterNemPrivado()
    {
        var emailProperty = typeof(User).GetProperty(nameof(User.Email))!;

        emailProperty.CanWrite.Should().BeFalse(
            "Email é imutável de forma estrutural (CA-08, RN-USER-03) — não existe setter, nem privado, só o construtor");
    }

    [Fact] // CA-08
    public void User_NaoTemNenhumMetodoPublicoQueAlterEEmail()
    {
        var metodosQueAlteramEmail = typeof(User)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => !method.IsSpecialName) // exclui get_Email/getters de propriedade
            .Where(method => method.Name.Contains("Email", StringComparison.OrdinalIgnoreCase));

        metodosQueAlteramEmail.Should().BeEmpty(
            "não pode existir nenhum caminho de código (método) que altere Email após a criação (CA-08)");
    }
}
