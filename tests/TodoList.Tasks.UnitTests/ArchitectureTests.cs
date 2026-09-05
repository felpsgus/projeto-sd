using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using NetArchTest.Rules;
using TodoList.Tasks.Application;
using TodoList.Tasks.Domain;
using TodoList.Tasks.Infrastructure;
using Xunit;

namespace TodoList.Tasks.UnitTests;

/// <summary>
/// Testes de arquitetura do Tasks Service — CA-05, CA-06, CA-06b e CA-06c de BE-01.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly _domainAssembly = typeof(TasksDomainAssemblyMarker).Assembly;
    private static readonly Assembly _applicationAssembly = typeof(TasksApplicationAssemblyMarker).Assembly;
    private static readonly Assembly _infrastructureAssembly = typeof(TasksInfrastructureAssemblyMarker).Assembly;

    [Fact]
    public void Domain_NaoReferenciaCamadasExternas()
    {
        var result = Types.InAssembly(_domainAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "TodoList.Tasks.Application",
                "TodoList.Tasks.Infrastructure",
                "TodoList.Tasks.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(DescreverFalhas(result));
    }

    [Fact]
    public void Application_NaoReferenciaInfrastructureOuApi()
    {
        var result = Types.InAssembly(_applicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "TodoList.Tasks.Infrastructure",
                "TodoList.Tasks.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(DescreverFalhas(result));
    }

    [Fact]
    public void Infrastructure_NaoReferenciaApi()
    {
        var result = Types.InAssembly(_infrastructureAssembly)
            .Should()
            .NotHaveDependencyOn("TodoList.Tasks.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(DescreverFalhas(result));
    }

    [Fact] // CA-06
    public void Domain_NaoTemPackageReference_ETemApenasProjectReferenceParaSharedKernel()
    {
        var csprojPath = Path.Combine(
            SolutionPathHelper.SolutionRoot, "src", "Tasks", "TodoList.Tasks.Domain", "TodoList.Tasks.Domain.csproj");

        var document = XDocument.Load(csprojPath);

        var packageReferences = document.Descendants("PackageReference").ToList();
        var projectReferences = document.Descendants("ProjectReference").ToList();

        packageReferences.Should().BeEmpty("Domain não deve depender de nenhum pacote externo");
        projectReferences.Should().ContainSingle("Domain deve referenciar apenas o SharedKernel");
        projectReferences[0].Attribute("Include")!.Value.Should().EndWith("TodoList.SharedKernel.csproj");
    }

    [Fact] // CA-06b
    public void SharedKernel_NaoTemNenhumaReferencia()
    {
        var csprojPath = Path.Combine(
            SolutionPathHelper.SolutionRoot, "src", "Shared", "TodoList.SharedKernel", "TodoList.SharedKernel.csproj");

        var document = XDocument.Load(csprojPath);

        document.Descendants("PackageReference").Should().BeEmpty();
        document.Descendants("ProjectReference").Should().BeEmpty();
    }

    [Fact] // CA-06c
    public void NenhumProjetoDoTasks_ReferenciaProjetoDoIdentity()
    {
        var identitySrc = Path.Combine(SolutionPathHelper.SolutionRoot, "src", "Identity");
        var tasksSrc = Path.Combine(SolutionPathHelper.SolutionRoot, "src", "Tasks");

        var referenciasIndevidas = new List<string>();

        foreach (var csproj in Directory.EnumerateFiles(tasksSrc, "*.csproj", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(csproj);
            var directory = Path.GetDirectoryName(csproj)!;

            foreach (var reference in document.Descendants("ProjectReference"))
            {
                var includePath = reference.Attribute("Include")!.Value;
                var resolvedPath = Path.GetFullPath(Path.Combine(directory, includePath));

                if (resolvedPath.StartsWith(identitySrc, StringComparison.OrdinalIgnoreCase))
                {
                    referenciasIndevidas.Add($"{csproj} -> {includePath}");
                }
            }
        }

        referenciasIndevidas.Should().BeEmpty();
    }

    [Fact] // CA-08 de BE-25
    public void DomainEApplication_NaoReferenciamOProtoNemOsTiposGerados()
    {
        var csprojsSemProtobuf = new[]
        {
            Path.Combine(SolutionPathHelper.SolutionRoot, "src", "Tasks", "TodoList.Tasks.Domain", "TodoList.Tasks.Domain.csproj"),
            Path.Combine(SolutionPathHelper.SolutionRoot, "src", "Tasks", "TodoList.Tasks.Application", "TodoList.Tasks.Application.csproj"),
        };

        foreach (var csproj in csprojsSemProtobuf)
        {
            var document = XDocument.Load(csproj);

            document.Descendants("Protobuf").Should().BeEmpty($"{csproj} não pode gerar código a partir do .proto — isso é assunto da borda");
        }

        var result = Types.InAssembly(_domainAssembly)
            .That()
            .ResideInNamespace("TodoList.Tasks.Domain")
            .Should()
            .NotHaveDependencyOn("TodoList.Contracts.Identity.V1")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(DescreverFalhas(result));

        var applicationResult = Types.InAssembly(_applicationAssembly)
            .That()
            .ResideInNamespace("TodoList.Tasks.Application")
            .Should()
            .NotHaveDependencyOn("TodoList.Contracts.Identity.V1")
            .GetResult();

        applicationResult.IsSuccessful.Should().BeTrue(DescreverFalhas(applicationResult));
    }

    [Fact] // CA-02, CA-03 de BE-27
    public void DomainEApplication_NaoReferenciamGrpcNemProtobuf_NosCsproj()
    {
        var csprojsSemGrpc = new[]
        {
            Path.Combine(SolutionPathHelper.SolutionRoot, "src", "Tasks", "TodoList.Tasks.Domain", "TodoList.Tasks.Domain.csproj"),
            Path.Combine(SolutionPathHelper.SolutionRoot, "src", "Tasks", "TodoList.Tasks.Application", "TodoList.Tasks.Application.csproj"),
        };

        foreach (var csproj in csprojsSemGrpc)
        {
            var document = XDocument.Load(csproj);

            var referenciasProibidas = document.Descendants("PackageReference")
                .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
                .Where(include => include.StartsWith("Grpc.", StringComparison.OrdinalIgnoreCase)
                    || include.Equals("Google.Protobuf", StringComparison.OrdinalIgnoreCase))
                .ToList();

            referenciasProibidas.Should().BeEmpty($"{csproj} não pode referenciar Grpc.* nem Google.Protobuf");
        }
    }

    [Fact] // CA-02 de BE-27
    public void DomainEApplication_NaoReferenciamTiposGeradosPeloProto_MesmoForaDoNamespaceDoContrato()
    {
        var result = Types.InAssembly(_domainAssembly)
            .Should()
            .NotHaveDependencyOnAny("Grpc.Core", "Grpc.Net.Client", "Grpc.Net.ClientFactory", "Google.Protobuf")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(DescreverFalhas(result));

        var applicationResult = Types.InAssembly(_applicationAssembly)
            .Should()
            .NotHaveDependencyOnAny("Grpc.Core", "Grpc.Net.Client", "Grpc.Net.ClientFactory", "Google.Protobuf")
            .GetResult();

        applicationResult.IsSuccessful.Should().BeTrue(DescreverFalhas(applicationResult));
    }

    [Fact] // CA-06 de BE-27
    public void CodigoDoTasks_NaoContemEnderecoOuPortaLiteral_ForaDosAppsettings()
    {
        var tasksSrc = Path.Combine(SolutionPathHelper.SolutionRoot, "src", "Tasks");
        var violacoes = new List<string>();

        foreach (var arquivo in Directory.EnumerateFiles(tasksSrc, "*.cs", SearchOption.AllDirectories))
        {
            if (arquivo.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || arquivo.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var conteudo = File.ReadAllText(arquivo);

            if (conteudo.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                || conteudo.Contains("http://", StringComparison.OrdinalIgnoreCase))
            {
                violacoes.Add(arquivo);
            }
        }

        violacoes.Should().BeEmpty("endereço/porta do Identity deve vir só de appsettings*.json (Identity:GrpcAddress), nunca de código");
    }

    [Fact] // CA-10 de BE-03
    public void SharedKernel_ExpoeApenasResultErrorEErrorType()
    {
        var sharedKernelAssembly = typeof(TodoList.SharedKernel.Result).Assembly;

        var nomesDosTiposPublicos = sharedKernelAssembly.GetExportedTypes()
            .Select(type => type.Name)
            .ToList();

        var nomesEsperados = new[] { "Result", "Result`1", "Error", "ErrorType" };

        nomesDosTiposPublicos.Should().BeEquivalentTo(
            nomesEsperados,
            "SharedKernel (D-26) não pode conter entidade, DTO de negócio, catálogo de erros ou regra");
    }

    [Fact] // CA-12 de BE-02
    public void Application_NaoReferenciaEntityFrameworkCoreNemNpgsql()
    {
        var result = Types.InAssembly(_applicationAssembly)
            .Should()
            .NotHaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(DescreverFalhas(result));

        var csprojPath = Path.Combine(
            SolutionPathHelper.SolutionRoot, "src", "Tasks", "TodoList.Tasks.Application", "TodoList.Tasks.Application.csproj");
        var document = XDocument.Load(csprojPath);

        var referenciasProibidas = document.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(include => include.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
                || include.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase))
            .ToList();

        referenciasProibidas.Should().BeEmpty(
            "a Application não pode referenciar EF Core nem Npgsql, nem por PackageReference direto (CA-12) — só por IUnitOfWork");
    }

    private static string DescreverFalhas(TestResult result) =>
        result.FailingTypeNames is null
            ? "sem detalhes disponíveis"
            : string.Join(", ", result.FailingTypeNames);
}
