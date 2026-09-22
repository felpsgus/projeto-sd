using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>
/// BE-40, CA-16 — subir o Gateway sem <c>Jwt:PublicKeyPath</c>, ou apontando
/// para um arquivo inexistente/não-PEM, falha na inicialização
/// (<c>ValidateOnStart</c>), nunca na primeira requisição. Diferente de
/// <see cref="GatewayApiFactory"/> (usado pelas outras suítes, sempre com uma
/// configuração válida), cada teste aqui cria seu próprio
/// <see cref="WebApplicationFactory{TEntryPoint}"/> com uma configuração
/// propositalmente quebrada — acessar <c>.Services</c> força a construção do
/// host e propaga a exceção de <c>ValidateOnStart</c>.
///
/// Só o comportamento de ponta a ponta é verificado aqui — o start falha, com
/// qualquer exceção, para os três cenários. A mensagem de erro (que precisa
/// nomear <c>Jwt:PublicKeyPath</c>, nunca conteúdo de arquivo) é verificada à
/// parte, sem host, em
/// <c>TodoList.Gateway.UnitTests.JwtOptionsValidationTests</c>: quando
/// <c>ValidateOnStart</c> reprova a configuração, o
/// <see cref="WebApplicationFactory{TEntryPoint}"/> tem duas exceções em
/// disputa — a <c>OptionsValidationException</c>, que traz a mensagem, e um
/// <see cref="ObjectDisposedException"/> do <c>IServiceProvider</c> já
/// descartado durante a falha de start. Qual delas chega ao teste depende de
/// tempo (mais provável sob carga, como ao rodar a solução inteira), então
/// afirmar aqui só "falhou" — nunca o tipo nem o texto da exceção — é o que
/// torna a suíte determinista sem perder cobertura.
/// </summary>
public class JwtStartupValidationTests
{
    [Theory] // CA-16
    [MemberData(nameof(ConfiguracoesInvalidas))]
    public void Startup_ComJwtPublicKeyPathInvalido_FalhaNaInicializacao(string cenario, string? arquivoTemporario)
    {
        _ = cenario;

        try
        {
            using var factory = CreateFactory(new Dictionary<string, string?> { ["Jwt:PublicKeyPath"] = arquivoTemporario });

            var act = () => _ = factory.Services;

            act.Should().Throw<Exception>();
        }
        finally
        {
            if (arquivoTemporario is not null)
            {
                File.Delete(arquivoTemporario);
            }
        }
    }

    /// <summary>
    /// Cada item é (nome do cenário, caminho de <c>Jwt:PublicKeyPath</c> —
    /// <c>null</c> para o cenário "ausente"). Para o cenário "arquivo
    /// não-PEM" o arquivo já existe com conteúdo inválido quando o teste
    /// roda; para "arquivo inexistente" o caminho nunca é criado.
    /// </summary>
    public static IEnumerable<object?[]> ConfiguracoesInvalidas()
    {
        yield return ["ausente", null];

        yield return ["arquivo inexistente", Path.Combine(Path.GetTempPath(), $"nao-existe-{Guid.NewGuid():N}.pem")];

        var caminhoNaoPem = Path.Combine(Path.GetTempPath(), $"nao-pem-{Guid.NewGuid():N}.pem");
        File.WriteAllText(caminhoNaoPem, "isto não é um PEM de chave nenhuma");
        yield return ["arquivo não-PEM", caminhoNaoPem];
    }

    /// <summary>
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> mínima, com
    /// configuração base válida (Backends + Jwt:Issuer/Audience) e um
    /// override pontual de <c>Jwt:PublicKeyPath</c> por cenário — nunca
    /// reaproveita <see cref="GatewayApiFactory"/> porque a falha de start
    /// aqui é o próprio comportamento sob teste, não algo a evitar.
    /// </summary>
    private static WebApplicationFactory<Program> CreateFactory(Dictionary<string, string?> jwtOverrides)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["Backends:IdentityGrpcAddress"] = "http://fake-identity",
                    ["Backends:TasksGrpcAddress"] = "http://fake-tasks",
                    ["Backends:IdentityGrpcTimeoutSeconds"] = "2",
                    ["Backends:TasksGrpcTimeoutSeconds"] = "5",
                    ["Jwt:Issuer"] = JwtTestTokens.DefaultIssuer,
                    ["Jwt:Audience"] = JwtTestTokens.DefaultAudience,
                };

                foreach (var (key, value) in jwtOverrides)
                {
                    settings[key] = value;
                }

                configuration.AddInMemoryCollection(settings);
            });
        });
    }
}
