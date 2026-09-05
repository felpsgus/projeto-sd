using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using NetArchTest.Rules;
using TodoList.Identity.Application;
using TodoList.Identity.Domain;
using TodoList.Identity.Infrastructure;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// Testes de arquitetura do Identity Service — CA-05, CA-06, CA-06b e CA-06c de BE-01.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly _domainAssembly = typeof(IdentityDomainAssemblyMarker).Assembly;
    private static readonly Assembly _applicationAssembly = typeof(IdentityApplicationAssemblyMarker).Assembly;
    private static readonly Assembly _infrastructureAssembly = typeof(IdentityInfrastructureAssemblyMarker).Assembly;

    [Fact]
    public void Domain_NaoReferenciaCamadasExternas()
    {
        var result = Types.InAssembly(_domainAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "TodoList.Identity.Application",
                "TodoList.Identity.Infrastructure",
                "TodoList.Identity.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(DescreverFalhas(result));
    }

    [Fact]
    public void Application_NaoReferenciaInfrastructureOuApi()
    {
        var result = Types.InAssembly(_applicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "TodoList.Identity.Infrastructure",
                "TodoList.Identity.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(DescreverFalhas(result));
    }

    [Fact]
    public void Infrastructure_NaoReferenciaApi()
    {
        var result = Types.InAssembly(_infrastructureAssembly)
            .Should()
            .NotHaveDependencyOn("TodoList.Identity.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(DescreverFalhas(result));
    }

    [Fact] // CA-06
    public void Domain_NaoTemPackageReference_ETemApenasProjectReferenceParaSharedKernel()
    {
        var csprojPath = Path.Combine(
            SolutionPathHelper.SolutionRoot, "src", "Identity", "TodoList.Identity.Domain", "TodoList.Identity.Domain.csproj");

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
    public void NenhumProjetoDoIdentity_ReferenciaProjetoDoTasks()
    {
        var identitySrc = Path.Combine(SolutionPathHelper.SolutionRoot, "src", "Identity");
        var tasksSrc = Path.Combine(SolutionPathHelper.SolutionRoot, "src", "Tasks");

        var referenciasIndevidas = new List<string>();

        foreach (var csproj in Directory.EnumerateFiles(identitySrc, "*.csproj", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(csproj);
            var directory = Path.GetDirectoryName(csproj)!;

            foreach (var reference in document.Descendants("ProjectReference"))
            {
                var includePath = reference.Attribute("Include")!.Value;
                var resolvedPath = Path.GetFullPath(Path.Combine(directory, includePath));

                if (resolvedPath.StartsWith(tasksSrc, StringComparison.OrdinalIgnoreCase))
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
            Path.Combine(SolutionPathHelper.SolutionRoot, "src", "Identity", "TodoList.Identity.Domain", "TodoList.Identity.Domain.csproj"),
            Path.Combine(SolutionPathHelper.SolutionRoot, "src", "Identity", "TodoList.Identity.Application", "TodoList.Identity.Application.csproj"),
        };

        foreach (var csproj in csprojsSemProtobuf)
        {
            var document = XDocument.Load(csproj);

            document.Descendants("Protobuf").Should().BeEmpty($"{csproj} não pode gerar código a partir do .proto — isso é assunto da borda");
        }

        var result = Types.InAssembly(_domainAssembly)
            .That()
            .ResideInNamespace("TodoList.Identity.Domain")
            .Should()
            .NotHaveDependencyOn("TodoList.Contracts.Identity.V1")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(DescreverFalhas(result));

        var applicationResult = Types.InAssembly(_applicationAssembly)
            .That()
            .ResideInNamespace("TodoList.Identity.Application")
            .Should()
            .NotHaveDependencyOn("TodoList.Contracts.Identity.V1")
            .GetResult();

        applicationResult.IsSuccessful.Should().BeTrue(DescreverFalhas(applicationResult));
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
            SolutionPathHelper.SolutionRoot, "src", "Identity", "TodoList.Identity.Application", "TodoList.Identity.Application.csproj");
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
