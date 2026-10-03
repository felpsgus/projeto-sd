using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>BE-24, CA-22 — todo endpoint de negócio do Gateway está documentado no OpenAPI com summary, descrição e respostas de erro.</summary>
public class OpenApiDocumentTests : IClassFixture<GatewayApiFactory>
{
    private static readonly string[] _httpMethods = ["get", "post", "put", "patch", "delete"];

    private readonly GatewayApiFactory _factory;

    public OpenApiDocumentTests(GatewayApiFactory factory)
    {
        _factory = factory;
    }

    [Fact] // CA-22
    public async Task Toda_operacao_de_negocio_tem_summary_descricao_e_resposta_de_erro_documentada()
    {
        var document = await LoadDocumentAsync();

        var operations = Operations(document).Where(operation => !operation.Path.StartsWith("/health", StringComparison.Ordinal)).ToList();

        operations.Should().HaveCount(16, "5 de auth + 4 de usuário + 7 de tarefas; endpoint novo precisa entrar aqui já documentado");

        foreach (var (path, method, operation) in operations)
        {
            var name = $"{method.ToUpperInvariant()} {path}";

            operation.TryGetProperty("summary", out var summary).Should().BeTrue("{0} precisa de summary", name);
            summary.GetString().Should().NotBeNullOrWhiteSpace();
            operation.TryGetProperty("description", out var description).Should().BeTrue("{0} precisa de description", name);
            description.GetString().Should().NotBeNullOrWhiteSpace();

            var statuses = operation.GetProperty("responses").EnumerateObject().Select(response => response.Name).ToList();
            statuses.Should().Contain(status => status.StartsWith('2'), "{0} precisa do código de sucesso", name);
            statuses.Should().Contain(status => status.StartsWith('4') || status.StartsWith('5'), "{0} precisa de ao menos uma resposta de erro", name);
            statuses.Should().Contain("503", "{0}: Identity/Tasks indisponível é 503 (fail-closed, D-28)", name);
        }
    }

    [Fact] // CA-22 — as rotas autenticadas documentam 401; login documenta 429 (bloqueio, ADR 0002)
    public async Task Codigos_de_resposta_chave_estao_declarados()
    {
        var document = await LoadDocumentAsync();
        var operations = Operations(document).ToDictionary(operation => $"{operation.Method} {operation.Path}", operation => operation.Operation);

        Statuses(operations["post /api/auth/login"]).Should().Contain(["200", "400", "401", "429", "503"]);
        Statuses(operations["post /api/auth/register"]).Should().Contain(["201", "400", "409", "503"]);
        Statuses(operations["post /api/tasks"]).Should().Contain(["201", "400", "401", "404", "409", "503"]);
        Statuses(operations["delete /api/tasks/{id}"]).Should().Contain(["204", "400", "401", "404", "503"]);

        // PUT é substituição (BE-19): o OpenAPI é onde isso precisa estar escrito.
        operations["put /api/tasks/{id}"].GetProperty("description").GetString().Should().Contain("SUBSTITUIÇÃO").And.Contain("omitidos viram null");
    }

    [Fact] // BE-08 CA-12 — o Scalar só oferece "Authorize" se o documento declara o esquema Bearer
    public async Task Esquema_bearer_e_exigido_so_nas_operacoes_autenticadas()
    {
        var document = await LoadDocumentAsync();
        string[] anonymous = ["post /api/auth/login", "post /api/auth/refresh", "post /api/auth/register", "get /health", "get /health/live"];

        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        scheme.GetProperty("type").GetString().Should().Be("http");
        scheme.GetProperty("scheme").GetString().Should().Be("bearer");
        scheme.GetProperty("bearerFormat").GetString().Should().Be("JWT");

        var operations = Operations(document).ToList();
        operations.Select(operation => $"{operation.Method} {operation.Path}").Should().Contain(["post /api/tasks", "get /api/tasks", "get /api/me", "post /api/auth/logout", "post /api/auth/logout-all"]);

        foreach (var (path, method, operation) in operations)
        {
            var name = $"{method} {path}";
            var hasBearer = operation.TryGetProperty("security", out var security)
                && security.EnumerateArray().Any(requirement => requirement.TryGetProperty("Bearer", out _));

            hasBearer.Should().Be(!anonymous.Contains(name), "{0}: Bearer só nas rotas autenticadas", name);
        }
    }

    private static IEnumerable<string> Statuses(JsonElement operation) =>
        operation.GetProperty("responses").EnumerateObject().Select(response => response.Name);

    private static IEnumerable<(string Path, string Method, JsonElement Operation)> Operations(JsonElement document) =>
        document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(item => _httpMethods.Contains(item.Name))
                .Select(item => (path.Name, item.Name, item.Value)));

    private async Task<JsonElement> LoadDocumentAsync()
    {
        // GatewayApiFactory sobe em Development, onde o documento é mapeado em /openapi/v1.json.
        var json = await _factory.CreateClient().GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        return JsonDocument.Parse(json).RootElement;
    }
}
