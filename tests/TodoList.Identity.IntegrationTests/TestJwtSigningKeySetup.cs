using System.Runtime.CompilerServices;

namespace TodoList.Identity.IntegrationTests;

/// <summary>
/// Ponto único de injeção de <c>Jwt:SigningKey</c> para os testes de
/// integração que sobem o Identity via <c>WebApplicationFactory&lt;Program&gt;</c>
/// (BE-08). Desde que a chave passou a ser obrigatória na inicialização
/// (<c>ValidateOnStart</c>), qualquer fábrica sem ela falharia ao subir. Em
/// vez de customizar <c>ConfigureWebHost</c> em cada classe de teste que usa
/// <c>IClassFixture&lt;WebApplicationFactory&lt;Program&gt;&gt;</c>, este
/// <see cref="ModuleInitializerAttribute"/> roda uma única vez, antes de
/// qualquer teste, e define a variável de ambiente do processo — o mesmo
/// mecanismo (<c>Jwt__SigningKey</c>) que a documentação recomenda para
/// produção, só que com um valor fixo de teste. Nunca em
/// <c>appsettings*.json</c> versionado.
/// </summary>
internal static class TestJwtSigningKeySetup
{
    /// <summary>
    /// Só para teste — 33 bytes ASCII (&gt; mínimo de 32 exigido por
    /// <c>JwtOptions</c>, CA-03 de BE-08), nunca usada fora deste processo.
    /// </summary>
    public const string SigningKey = "test-jwt-signing-key-32-bytes-ok!";

    [ModuleInitializer]
    public static void Initialize()
    {
        Environment.SetEnvironmentVariable("Jwt__SigningKey", SigningKey);
    }
}
