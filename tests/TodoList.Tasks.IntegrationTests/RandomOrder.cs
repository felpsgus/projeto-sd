using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestCaseOrderer(
    "TodoList.Tasks.IntegrationTests.RandomTestCaseOrderer",
    "TodoList.Tasks.IntegrationTests")]
[assembly: TestCollectionOrderer(
    "TodoList.Tasks.IntegrationTests.RandomTestCollectionOrderer",
    "TodoList.Tasks.IntegrationTests")]

namespace TodoList.Tasks.IntegrationTests;

/// <summary>
/// CA-10 de BE-02: "rodar a suíte em ordem aleatória duas vezes seguidas produz
/// o mesmo resultado". O xUnit roda em ordem fixa por padrão — sem isto o
/// critério nunca seria exercido, e uma dependência de ordem entre testes
/// (fáceis de criar quando eles compartilham um container Postgres) passaria
/// despercebida até virar falha intermitente no CI.
///
/// <para>
/// A semente é sorteada por execução e impressa no início: quando uma ordem
/// específica quebrar, dá para reproduzi-la com
/// <c>XUNIT_ORDER_SEED=&lt;valor&gt; dotnet test</c> em vez de tentar a sorte.
/// </para>
/// </summary>
internal static class RandomOrderSeed
{
    public const string EnvironmentVariable = "XUNIT_ORDER_SEED";

    private static readonly int _seed = ResolveSeed();

    public static Random CreateRandom() => new(_seed);

    private static int ResolveSeed()
    {
        var configurado = Environment.GetEnvironmentVariable(EnvironmentVariable);

        var seed = int.TryParse(configurado, out var valor) ? valor : Random.Shared.Next();

        Console.WriteLine(
            $"[TodoList.Tasks.IntegrationTests] ordem dos testes aleatória, semente {seed} "
            + $"(reproduza com {EnvironmentVariable}={seed})");

        return seed;
    }
}

/// <summary>Embaralha os testes dentro de cada classe (CA-10).</summary>
public sealed class RandomTestCaseOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        var random = RandomOrderSeed.CreateRandom();

        return testCases.OrderBy(_ => random.Next()).ToList();
    }
}

/// <summary>Embaralha as coleções entre si (CA-10).</summary>
public sealed class RandomTestCollectionOrderer : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
    {
        var random = RandomOrderSeed.CreateRandom();

        return testCollections.OrderBy(_ => random.Next()).ToList();
    }
}
