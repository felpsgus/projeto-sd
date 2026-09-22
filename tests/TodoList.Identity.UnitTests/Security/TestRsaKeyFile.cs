using System.Security.Cryptography;

namespace TodoList.Identity.UnitTests.Security;

/// <summary>
/// Gera um par RSA em memória (<see cref="RSA.Create(int)"/>) e grava só a
/// chave privada, em PEM PKCS8, num arquivo temporário — para os testes de
/// <see cref="JwtOptions"/>/<see cref="JwtTokenService"/>/<see cref="JwtAccessTokenValidator"/>
/// que precisam de um <c>Jwt:PrivateKeyPath</c> real (BE-40). Nenhuma chave
/// de teste é versionada: o arquivo vive em <see cref="Path.GetTempPath"/> e
/// é apagado em <see cref="Dispose"/>.
/// </summary>
internal sealed class TestRsaKeyFile : IDisposable
{
    public string Path { get; }

    private TestRsaKeyFile(string path) => Path = path;

    public static TestRsaKeyFile Create(int keySizeInBits = 2048)
    {
        using var rsa = RSA.Create(keySizeInBits);
        var pem = rsa.ExportPkcs8PrivateKeyPem();

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"jwt-test-key-{Guid.NewGuid():N}.pem");
        File.WriteAllText(path, pem);

        return new TestRsaKeyFile(path);
    }

    public void Dispose()
    {
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }
    }
}
