using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace TodoList.Tasks.IntegrationTests;

/// <summary>
/// Ponto único de injeção de <c>Jwt:PrivateKeyPath</c> (BE-40, D-38) para os
/// testes deste projeto que sobem um Identity real via
/// <c>WebApplicationFactory&lt;IdentityProgram&gt;</c> (BE-27/BE-28/BE-35) —
/// o Identity passou a exigir uma chave RSA assimétrica na inicialização
/// (<c>ValidateOnStart</c> de <c>JwtOptions</c>, BE-40), e este projeto não
/// referencia <c>TodoList.Identity.IntegrationTests</c> (só
/// <c>TodoList.Identity.Api</c>, por alias), então o
/// <c>ModuleInitializer</c> equivalente daquele projeto não roda aqui. Mesma
/// técnica: gera um par RSA 2048 em memória, grava só a chave privada — em
/// PEM PKCS8 — num arquivo temporário do processo de teste (nunca
/// versionado) e define a variável de ambiente do processo uma única vez,
/// antes de qualquer teste. O Tasks Service em si não lê nem valida esta
/// chave (D-38: só o Identity assina, só o Gateway valida com a pública) —
/// ela existe aqui só para permitir que o Identity real suba dentro do
/// processo de teste.
///
/// <para>
/// Também fixa <c>UserStore:Provider=InMemory</c> como padrão para o
/// Identity subido aqui: com <c>Persisted</c> virando o padrão de produção
/// (D-39), os testes deste projeto que esperam o seed em memória
/// (<c>InMemoryUserLookup.SeedUserId</c>, ex.:
/// <see cref="GrpcIdentityGatewayIntegrationTests"/>) precisam continuar
/// recebendo <c>InMemory</c> sem configurar nada extra.
/// </para>
/// </summary>
internal static class TestIdentityJwtSigningKeySetup
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

        var path = Path.Combine(Path.GetTempPath(), $"tasks-integration-tests-identity-jwt-key-{Guid.NewGuid():N}.pem");
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
