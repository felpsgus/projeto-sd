using FluentAssertions;
using Microsoft.Extensions.Options;
using TodoList.Identity.Infrastructure.Security;
using Xunit;

namespace TodoList.Identity.UnitTests.Security;

/// <summary>
/// <see cref="Pbkdf2PasswordHasher"/> (BE-06) — CA-01 a CA-05 e CA-08/CA-09.
/// Custo reduzido (1_000 iterações) via <see cref="PasswordHashingOptions"/>
/// para os testes rodarem rápido — nunca um hasher fake que não faz hash.
/// </summary>
public class Pbkdf2PasswordHasherTests
{
    private const string TestIterationsIndicator = "1000";

    [Fact] // CA-01
    public void Hash_ChamadoDuasVezesComAMesmaSenha_ProduzHashesDiferentes()
    {
        var hasher = CreateHasher();

        var hash1 = hasher.Hash("SenhaForte123");
        var hash2 = hasher.Hash("SenhaForte123");

        hash1.Should().NotBe(hash2, "o salt é aleatório por chamada");
    }

    [Fact] // CA-02
    public void Verify_ComSenhaCorreta_RetornaTrue()
    {
        var hasher = CreateHasher();
        var hash = hasher.Hash("SenhaForte123");

        hasher.Verify("SenhaForte123", hash).Should().BeTrue();
    }

    [Fact] // CA-02
    public void Verify_ComSenhaErrada_RetornaFalse()
    {
        var hasher = CreateHasher();
        var hash = hasher.Hash("SenhaForte123");

        hasher.Verify("SenhaErrada456", hash).Should().BeFalse();
    }

    [Theory] // CA-03
    [InlineData("")]
    [InlineData("hash-qualquer-sem-formato")]
    [InlineData("pbkdf2-sha256$abc$salt$hash")]
    [InlineData("pbkdf2-sha256$0$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$-1$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$1000$!!!naoEhBase64!!!$aGFzaA==")]
    [InlineData("pbkdf2-sha256$1000$c2FsdA==$!!!naoEhBase64!!!")]
    [InlineData("outro-algoritmo$1000$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$1000$c2FsdA==")]
    [InlineData("pbkdf2-sha256$1000$c2FsdA==$")] // BE-08 (revisão BE-06): segmento de hash vazio — Base64 vazio decodifica sem erro, mas não pode chegar ao Pbkdf2 com tamanho de saída 0
    [InlineData("pbkdf2-sha256$1000$$aGFzaA==")] // BE-08 (revisão BE-06): segmento de salt vazio
    [InlineData("pbkdf2-sha256$1000$$")] // BE-08 (revisão BE-06): salt e hash vazios
    [InlineData("seed-placeholder-not-a-real-hash-be-06-pending")] // DemoUserSeeder.PlaceholderPasswordHash (BE-33 substitui pelo hash real)
    public void Verify_ComHashMalformado_RetornaFalseSemLancar(string hashMalformado)
    {
        var hasher = CreateHasher();

        var act = () => hasher.Verify("qualquerSenha123", hashMalformado);

        act.Should().NotThrow();
        hasher.Verify("qualquerSenha123", hashMalformado).Should().BeFalse();
    }

    [Fact] // CA-04
    public void Hash_NaoContemASenhaEmFormaRecuperavel()
    {
        var hasher = CreateHasher();
        const string senha = "SenhaForte123";

        var hash = hasher.Hash(senha);

        hash.Should().NotContain(senha);
        hash.Should().NotContain(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(senha)));
        hash.Should().Contain(TestIterationsIndicator, "o hash é auto-descritivo e embute o custo usado");
    }

    [Fact] // CA-05
    public void Verify_ApósAumentarIteracoesNaConfiguracao_AindaAceitaHashAntigo()
    {
        var hasherAntigo = CreateHasher(iterations: 1_000);
        var hash = hasherAntigo.Hash("SenhaForte123");

        var hasherNovo = CreateHasher(iterations: 2_000);

        hasherNovo.Verify("SenhaForte123", hash).Should().BeTrue(
            "as iterações usadas na verificação vêm do próprio hash armazenado, não da configuração atual");
    }

    [Fact] // CA-08 — nenhum tipo deste fluxo guarda a senha em campo/propriedade exposta por ToString()
    public void PasswordHashingOptions_ToString_NaoExpoeSenha()
    {
        var options = new PasswordHashingOptions { Iterations = 1_000 };

        options.ToString().Should().NotContain("Senha");
    }

    private static Pbkdf2PasswordHasher CreateHasher(int iterations = 1_000) =>
        new(Options.Create(new PasswordHashingOptions { Iterations = iterations }));
}
