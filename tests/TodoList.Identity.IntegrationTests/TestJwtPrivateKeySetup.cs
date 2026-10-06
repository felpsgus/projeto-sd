using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace TodoList.Identity.IntegrationTests;

/// <summary>
/// Ponto único de injeção de <c>Jwt:PrivateKeyPath</c> (BE-40, D-38) para os
/// testes de integração que sobem o Identity via
/// <c>WebApplicationFactory&lt;Program&gt;</c>. Substitui o
/// <c>TestJwtSigningKeySetup</c> do HS256 (BE-08): desde que a chave passou a
/// ser assimétrica e obrigatória na inicialização (<c>ValidateOnStart</c> de
/// <c>JwtOptions</c>), qualquer fábrica sem ela falharia ao subir. Este
/// <see cref="ModuleInitializerAttribute"/> roda uma única vez, antes de
/// qualquer teste: gera um par RSA 2048 em memória, grava só a chave
/// privada — em PEM PKCS8 — num arquivo temporário do processo de teste
/// (nunca versionado) e define a variável de ambiente <c>Jwt__PrivateKeyPath</c>,
/// o mesmo mecanismo que a documentação recomenda para produção.
///
/// <para>
/// Também fixa <c>UserStore:Provider=InMemory</c> como padrão destes testes.
/// Antes de D-39, esse era o padrão do próprio <c>appsettings.json</c>; com
/// <c>Persisted</c> virando o padrão de produção, os testes que dependiam do
/// <c>InMemory</c> implícito (<see cref="ValidateUserGrpcTests"/>,
/// <see cref="HealthEndpointTests"/> etc.) precisam continuar recebendo
/// <c>InMemory</c> — por isso a variável de ambiente aqui — enquanto os que
/// exigem banco real (ex.: <c>ValidateUserGrpcComProviderPersistidoTests</c>)
/// sobrescrevem explicitamente via <c>WebApplicationFactory.ConfigureAppConfiguration</c>,
/// que é adicionado depois das variáveis de ambiente na pipeline de
/// configuração — e por isso vence.
/// </para>
/// </summary>
internal static class TestJwtPrivateKeySetup
{
    [ModuleInitializer]
    public static void Initialize()
    {
        var privateKeyPath = WritePrivateKeyToTempFile();

        Environment.SetEnvironmentVariable("Jwt__PrivateKeyPath", privateKeyPath);
        Environment.SetEnvironmentVariable("UserStore__Provider", "InMemory");

        // BE-23/BE-24: o expurgo em segundo plano nunca roda dentro dos hosts de teste - nenhum teste
        // pode depender de (nem ser perturbado por) um ciclo de DataRetentionWorker. Os testes do
        // purger chamam IRetentionPurger direto.
        Environment.SetEnvironmentVariable("Retention__Enabled", "false");

        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDelete(privateKeyPath);
    }

    private static string WritePrivateKeyToTempFile()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();

        var path = Path.Combine(Path.GetTempPath(), $"identity-integration-tests-jwt-key-{Guid.NewGuid():N}.pem");
        File.WriteAllText(path, pem);

        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort: arquivo temporário de teste, não crítico se sobrar.
        }
    }
}
