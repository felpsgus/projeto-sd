using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using TodoList.Gateway.Api.Backends;
using Xunit;

namespace TodoList.Gateway.UnitTests;

/// <summary>BE-36, CA-20/CA-21 — o Gateway não conhece Identity/Tasks/SharedKernel além dos dois .proto (D-33).</summary>
public class ArchitectureTests
{
    private static readonly string[] _forbiddenAssemblyPrefixes =
    [
        "TodoList.Identity",
        "TodoList.Tasks",
        "TodoList.SharedKernel",
    ];

    [Fact]
    public void GatewayAssembly_NaoReferenciaIdentityNemTasksNemSharedKernel() // CA-20
    {
        var gatewayAssembly = typeof(IIdentityBackend).Assembly;

        var referencedAssemblyNames = gatewayAssembly.GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name!)
            .ToList();

        referencedAssemblyNames.Should().NotContain(
            name => _forbiddenAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)),
            "o Gateway (D-33) não deve ter ProjectReference nenhuma a Identity/Tasks/SharedKernel — só os dois .proto como cliente");
    }

    [Fact]
    public void GatewayCsproj_SoTemOsDoisProtobufClientEsperados() // CA-21
    {
        var csprojPath = FindGatewayCsproj();
        var document = XDocument.Load(csprojPath);

        var protobufItems = document.Descendants("Protobuf").ToList();

        protobufItems.Should().HaveCount(2);
        protobufItems.Should().OnlyContain(item => item.Attribute("GrpcServices")!.Value == "Client");

        var includes = protobufItems.Select(item => item.Attribute("Include")!.Value.Replace('\\', '/')).ToList();
        includes.Should().Contain(include => include.EndsWith("contracts/identity/v1/identity.proto", StringComparison.Ordinal));
        includes.Should().Contain(include => include.EndsWith("contracts/tasks/v1/tasks.proto", StringComparison.Ordinal));

        document.Descendants("ProjectReference").Should().BeEmpty(
            "o Gateway (D-33) não deve ter nenhuma ProjectReference a outro projeto TodoList.*");
    }

    private static string FindGatewayCsproj()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TodoList.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Não foi possível localizar a raiz do repositório (TodoList.sln).");
        }

        return Path.Combine(directory.FullName, "src", "Gateway", "TodoList.Gateway.Api", "TodoList.Gateway.Api.csproj");
    }
}
