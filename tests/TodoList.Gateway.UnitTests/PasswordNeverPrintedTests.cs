using System.Reflection;
using FluentAssertions;
using TodoList.Gateway.Api.Contracts;
using Xunit;

namespace TodoList.Gateway.UnitTests;

/// <summary>
/// BE-06, CA-08 — nenhum tipo do Gateway com propriedade de senha em texto puro
/// imprime o valor no <c>ToString()</c> (o gerado por <c>record</c> e o das
/// mensagens protobuf imprimem todas as propriedades). Achado por reflexão, para
/// que um tipo novo com <c>*Password*</c> entre na checagem sozinho.
/// Mensagens protobuf geradas ficam de fora: o <c>ToString</c> delas é gerado e
/// não pode ser sobrescrito (nem o C# honra <c>debug_redact</c>); o que as protege é o
/// fato de nunca serem logadas (<c>LogLeakageTests</c>).
/// </summary>
public class PasswordNeverPrintedTests
{
    private const string Sentinel = "SENTINELA-SENHA-9f3a";

    [Fact]
    public void ToString_TiposComPropriedadeDeSenha_NaoImprimemOValor()
    {
        var assemblies = new[] { typeof(LoginHttpRequest).Assembly };

        var vazados = assemblies.SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericType: false } && !typeof(Google.Protobuf.IMessage).IsAssignableFrom(t) && PasswordProperties(t).Any())
            .Select(t => (Type: t, Instance: Create(t)))
            .Where(x => x.Instance is not null)
            .Where(x => x.Instance!.ToString()!.Contains(Sentinel, StringComparison.Ordinal))
            .Select(x => x.Type.FullName)
            .ToList();

        vazados.Should().BeEmpty("senha em texto puro não pode aparecer em log via ToString()");
    }

    // "Hash" fica fora: PasswordHash é derivado, não a senha.
    private static IEnumerable<PropertyInfo> PasswordProperties(Type type) =>
        type.GetProperties().Where(p => p.PropertyType == typeof(string) && p.Name.Contains("Password", StringComparison.Ordinal) && !p.Name.Contains("Hash", StringComparison.Ordinal));

    private static object? Create(Type type)
    {
        var ctor = type.GetConstructors().Where(c => c.GetParameters().All(p => p.ParameterType != type)).OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
        if (ctor is null)
        {
            return null;
        }

        var args = ctor.GetParameters()
            .Select(p => p.Name!.Contains("Password", StringComparison.OrdinalIgnoreCase) ? Sentinel
                : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
            .ToArray();
        var instance = ctor.Invoke(args);

        foreach (var property in PasswordProperties(type).Where(p => p.SetMethod?.IsPublic == true))
        {
            property.SetValue(instance, Sentinel);
        }

        return instance;
    }
}
