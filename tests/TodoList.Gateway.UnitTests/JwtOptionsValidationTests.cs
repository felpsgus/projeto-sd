using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using TodoList.Gateway.Api.Configuration;
using Xunit;

namespace TodoList.Gateway.UnitTests;

/// <summary>
/// BE-40, CA-16 — <see cref="JwtOptions.Validate"/> (chamada por
/// <c>ValidateDataAnnotations</c>/<c>ValidateOnStart</c> na inicialização real
/// do Gateway) nomeia sempre <c>Jwt:PublicKeyPath</c> na mensagem de erro, nos
/// três cenários — ausente, arquivo inexistente, arquivo não-PEM — e nunca
/// expõe conteúdo de arquivo. Testado aqui como validação pura, sem host: os
/// testes de <see cref="TodoList.Gateway.IntegrationTests.JwtStartupValidationTests"/>
/// cobrem o comportamento de ponta a ponta (falhar no start), mas afirmar o
/// texto exato da mensagem contra um <c>WebApplicationFactory</c> real é
/// instável — a falha de <c>ValidateOnStart</c> compete com um
/// <see cref="ObjectDisposedException"/> do provider já descartado, e qual
/// das duas chega ao teste depende de tempo (mais provável sob carga, como ao
/// rodar a solução inteira). Validar a options diretamente elimina a
/// competição por completo.
/// </summary>
public class JwtOptionsValidationTests
{
    [Fact] // CA-16
    public void Validate_SemPublicKeyPath_FalhaNomeandoAChaveSemExporConteudo()
    {
        var options = new JwtOptions
        {
            Issuer = "issuer",
            Audience = "audience",
            PublicKeyPath = string.Empty,
        };

        var results = Validate(options);

        results.Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("Jwt:PublicKeyPath");
    }

    [Fact] // CA-16
    public void Validate_PublicKeyPathApontaParaArquivoInexistente_FalhaNomeandoAChave()
    {
        var caminhoInexistente = Path.Combine(Path.GetTempPath(), $"nao-existe-{Guid.NewGuid():N}.pem");

        var options = new JwtOptions
        {
            Issuer = "issuer",
            Audience = "audience",
            PublicKeyPath = caminhoInexistente,
        };

        var results = Validate(options);

        results.Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("Jwt:PublicKeyPath");
    }

    [Fact] // CA-16
    public void Validate_PublicKeyPathApontaParaArquivoNaoPem_FalhaNomeandoAChaveSemExporConteudo()
    {
        var caminhoInvalido = Path.Combine(Path.GetTempPath(), $"nao-pem-{Guid.NewGuid():N}.pem");
        const string conteudoDoArquivo = "isto não é um PEM de chave nenhuma";
        File.WriteAllText(caminhoInvalido, conteudoDoArquivo);

        try
        {
            var options = new JwtOptions
            {
                Issuer = "issuer",
                Audience = "audience",
                PublicKeyPath = caminhoInvalido,
            };

            var results = Validate(options);

            var mensagem = results.Should().ContainSingle().Which.ErrorMessage!;
            mensagem.Should().Contain("Jwt:PublicKeyPath");
            mensagem.Should().NotContain(conteudoDoArquivo);
        }
        finally
        {
            File.Delete(caminhoInvalido);
        }
    }

    /// <summary>
    /// Chama <see cref="JwtOptions.Validate"/> exatamente como
    /// <c>ValidateDataAnnotations</c> faria — sem depender de host, DI ou
    /// configuração — para isolar a asserção de mensagem do comportamento de
    /// inicialização (coberto separadamente na suíte de integração).
    /// </summary>
    private static List<ValidationResult> Validate(JwtOptions options)
        => options.Validate(new ValidationContext(options)).ToList();
}
