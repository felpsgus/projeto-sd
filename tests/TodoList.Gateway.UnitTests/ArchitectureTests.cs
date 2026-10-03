using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using TodoList.Gateway.Api.Backends;
using Xunit;

namespace TodoList.Gateway.UnitTests;

/// <summary>BE-36, CA-20/CA-21 — o Gateway não conhece Identity/Tasks/SharedKernel além dos dois .proto (D-33).</summary>
public class ArchitectureTests
{
    [Fact] // BE-40, CA-19
    public void GatewayAssembly_NaoContemMaisOEsquemaIdentityTokenNemOHandler()
    {
        var gatewayAssembly = typeof(IIdentityBackend).Assembly;

        var typeNames = gatewayAssembly.GetTypes()
            .Select(type => type.FullName)
            .Where(name => name is not null)
            .Cast<string>()
            .ToList();

        typeNames.Should().NotContain(
            name => name.Contains("IdentityTokenAuthenticationHandler", StringComparison.Ordinal),
            "IdentityTokenAuthenticationHandler foi removido por BE-40/D-38 — AddJwtBearer substitui a validação via gRPC");

        typeNames.Should().NotContain(
            name => name.Contains("IdentityAuthenticationDefaults", StringComparison.Ordinal),
            "o esquema \"IdentityToken\" foi removido por BE-40/D-38 — o esquema atual é JwtBearerDefaults.AuthenticationScheme");
    }

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

    [Fact] // BE-25 CA-06 / BE-32 CA-07
    public void ContratosProto_TodoRpcECampoTemComentario()
    {
        var declaration = new Regex(@"^\s*(rpc\s|((optional|repeated)\s+)?([\w.]+\s+)?\w+\s*=\s*\d+\s*;)");
        var uncommented = new List<string>();

        foreach (var proto in new[] { "identity/v1/identity.proto", "tasks/v1/tasks.proto" })
        {
            var lines = File.ReadAllLines(Path.Combine(FindRepoRoot(), "contracts", proto));

            for (var i = 0; i < lines.Length; i++)
            {
                if (!declaration.IsMatch(lines[i]) || lines[i].Contains("//", StringComparison.Ordinal))
                {
                    continue;
                }

                var previous = lines.Take(i).LastOrDefault(line => line.Trim().Length > 0) ?? "";
                if (!previous.TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    uncommented.Add($"{proto}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        string.Join("\n", uncommented).Should().BeEmpty("o .proto é o contrato entre os serviços: todo rpc e campo precisa de comentário");
    }

    private static string FindGatewayCsproj() =>
        Path.Combine(FindRepoRoot(), "src", "Gateway", "TodoList.Gateway.Api", "TodoList.Gateway.Api.csproj");

    private static string FindRepoRoot()
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

        return directory.FullName;
    }
}
