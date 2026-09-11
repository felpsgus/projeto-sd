using System.Runtime.CompilerServices;

namespace TodoList.Tasks.IntegrationTests;

/// <summary>
/// Ponto único de injeção de <c>Jwt:SigningKey</c> para os testes deste
/// projeto que sobem um Identity real via <c>WebApplicationFactory&lt;IdentityProgram&gt;</c>
/// (BE-27/BE-28/BE-35) — o Identity passou a exigir a chave na inicialização
/// (<c>ValidateOnStart</c> de <c>JwtOptions</c>, BE-08), e este projeto não
/// referencia <c>TodoList.Identity.IntegrationTests</c> (só
/// <c>TodoList.Identity.Api</c>, por alias), então o
/// <c>ModuleInitializer</c> equivalente daquele projeto não roda aqui. Mesma
/// técnica: define a variável de ambiente do processo uma única vez, antes
/// de qualquer teste, nunca em <c>appsettings*.json</c> versionado. O Tasks
/// Service em si não lê nem valida esta chave (D-31: só o Identity é
/// autoridade sobre tokens) — ela existe aqui só para permitir que o
/// Identity real suba dentro do processo de teste.
/// </summary>
internal static class TestIdentityJwtSigningKeySetup
{
    /// <summary>Só para teste — 33 bytes ASCII (acima do mínimo de 32 exigido por <c>JwtOptions</c>), nunca usada fora deste processo.</summary>
    public const string SigningKey = "test-jwt-signing-key-32-bytes-ok!";

    [ModuleInitializer]
    public static void Initialize()
    {
        Environment.SetEnvironmentVariable("Jwt__SigningKey", SigningKey);
    }
}
