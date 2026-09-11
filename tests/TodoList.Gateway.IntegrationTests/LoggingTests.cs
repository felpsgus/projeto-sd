using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using TodoList.Gateway.Api.Contracts;
using Xunit;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>
/// BE-36, CA-26 — cada chamada gRPC de saída gera uma linha de log com
/// backend/rpc/statusCode/duração/traceId, e nenhuma delas carrega token,
/// senha ou corpo da requisição.
/// </summary>
public class LoggingTests : IClassFixture<GatewayApiFactory>
{
    private readonly GatewayApiFactory _factory;

    public LoggingTests(GatewayApiFactory factory)
    {
        _factory = factory;
    }

    [Fact] // CA-26 — Login: senha nunca aparece no log, mas a chamada gera uma linha
    public async Task Login_GeraUmaLinhaDeLogSemSenhaNoCorpo()
    {
        const string senhaSecreta = "SENHA-SECRETA-MARCADOR-9f8e7d";
        _factory.Identity.LoginHandler = (_, _) => (true, "token-marcador-abc123", DateTimeOffset.UtcNow.AddHours(1), Guid.NewGuid().ToString());

        var client = _factory.CreateClient();

        await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative), new LoginHttpRequest("user@example.com", senhaSecreta));

        var entries = _factory.Logs.Entries.ToList();

        entries.Should().Contain(entry => entry.Contains("backend=Identity") && entry.Contains("rpc=Login"));
        entries.Should().OnlyContain(entry => !entry.Contains(senhaSecreta), "nenhuma linha de log deve carregar a senha");
        entries.Should().OnlyContain(entry => !entry.Contains("token-marcador-abc123"), "nenhuma linha de log deve carregar o access token");
    }

    [Fact] // CA-26 — CreateTask: token de autorização e corpo da tarefa nunca aparecem no log
    public async Task CreateTask_GeraUmaLinhaDeLogSemTokenNemCorpo()
    {
        const string bearerToken = "TOKEN-BEARER-MARCADOR-1a2b3c";
        const string tituloSecreto = "Título Secreto Que Não Deve Vazar no Log";

        _factory.Identity.ValidateTokenHandler = _ => (true, Guid.NewGuid().ToString());

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        await client.PostAsJsonAsync(
            new Uri("/api/tasks", UriKind.Relative), new CreateTaskHttpRequest(tituloSecreto, null, null, null));

        var entries = _factory.Logs.Entries.ToList();

        entries.Should().Contain(entry => entry.Contains("backend=Identity") && entry.Contains("rpc=ValidateToken"));
        entries.Should().Contain(entry => entry.Contains("backend=Tasks") && entry.Contains("rpc=CreateTask"));
        entries.Should().OnlyContain(entry => !entry.Contains(bearerToken), "nenhuma linha de log deve carregar o token de autorização");
        entries.Should().OnlyContain(entry => !entry.Contains(tituloSecreto), "nenhuma linha de log deve carregar o corpo/título da tarefa");
    }
}
