extern alias IdentityApi;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TodoList.Identity.Infrastructure.Users;
using Xunit;
using IdentityProgram = IdentityApi::Program;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// <c>POST /api/tasks</c> — caminho de sucesso e validação de request
/// (BE-17). Roda no modo provisório de identidade (BE-29,
/// <c>Tasks:AllowAnonymousCreate=true</c>, header <c>X-User-Id</c>): é o
/// único jeito de exercitar o endpoint de ponta a ponta nesta etapa, já que
/// BE-13 (autenticação real) ainda não existe — ver o relatório de
/// BE-17/BE-28/BE-29 para o que fica em aberto por causa disso (CA-21 de
/// BE-17, CA-07/CA-08 de BE-29).
/// </summary>
public sealed class CreateTaskEndpointTests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _connection = null!;
    private FakeTimeProvider _timeProvider = null!;
    private WebApplicationFactory<IdentityProgram> _identityFactory = null!;
    private TasksApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 8, 21, 0, 30, 0, TimeSpan.Zero));
        _identityFactory = new WebApplicationFactory<IdentityProgram>();

        _factory = new TasksApiFactory(
            _connection,
            _timeProvider,
            _identityFactory,
            new Uri("http://identity.test"),
            new Dictionary<string, string?> { ["Tasks:AllowAnonymousCreate"] = "true" });

        await _factory.EnsureDatabaseCreatedAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _identityFactory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // CA1001: mesmo padrão de CreateTaskOwnerValidationTests — a disposição
    // de verdade acontece em DisposeAsync.
    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-01, CA-03
    public async Task PostTasks_ApenasComTitle_Retorna201ComStatusPendingECompletedAtNulo()
    {
        var response = await PostAsync(new { title = "Comprar leite" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Pending");
        body.GetProperty("completedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact] // CA-02 (Location + estado no banco — sem GET, ver nota abaixo)
    public async Task PostTasks_Retorna201ComLocationApontandoParaORecursoCriado()
    {
        var response = await PostAsync(new { title = "Tarefa com location" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();

        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Be($"/api/tasks/{id}");

        // Nota: BE-17 CA-02 pede também "o GET naquele endereço retorna a
        // tarefa". Não existe endpoint GET /api/tasks/{id} nesta base de
        // código — isso é BE-18 (fora do escopo desta rodada, que cobre só
        // BE-17/BE-28/BE-29; BE-17 explicitamente bloqueia, e não inclui,
        // BE-18). Em vez de inventar um endpoint de leitura fora de escopo,
        // a prova de "o recurso existe onde o Location aponta" é feita
        // direto no banco.
        await using var context = _factory.CreateDbContext();
        var persisted = await context.Tasks.SingleOrDefaultAsync(task => task.Id == id);
        persisted.Should().NotBeNull();
    }

    [Fact] // CA-04
    public async Task PostTasks_SemPriority_NasceComMedium()
    {
        var response = await PostAsync(new { title = "Sem prioridade" });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("priority").GetString().Should().Be("Medium");
    }

    [Fact] // CA-05 — valor de priority fora do enum, string
    public async Task PostTasks_ComPriorityInvalida_Retorna400()
    {
        var response = await PostAsync(new { title = "Prioridade inválida", priority = "Urgente" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact] // CA-05 — valor de priority válido é preservado
    public async Task PostTasks_ComPriorityHigh_PreservaOValor()
    {
        var response = await PostAsync(new { title = "Prioridade alta", priority = "High" });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("priority").GetString().Should().Be("High");
    }

    [Fact] // CA-06 — verificado no banco, RN-AUTZ-01
    public async Task PostTasks_TarefaCriada_TemOwnerIdIgualAoUsuarioCorrente()
    {
        var response = await PostAsync(new { title = "Tarefa com dono" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();

        await using var context = _factory.CreateDbContext();
        var persisted = await context.Tasks.SingleAsync(task => task.Id == id);

        persisted.OwnerId.Should().Be(InMemoryUserLookup.ActiveUserId);
    }

    [Fact] // CA-07
    public async Task PostTasks_CreatedAtEUpdatedAt_VemPreenchidosEIguaisNaCriacao()
    {
        var response = await PostAsync(new { title = "Auditoria" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var createdAt = body.GetProperty("createdAt").GetDateTime();
        var updatedAt = body.GetProperty("updatedAt").GetDateTime();

        createdAt.Should().Be(_timeProvider.GetUtcNow().UtcDateTime);
        updatedAt.Should().Be(createdAt);
    }

    [Fact] // CA-08 — vazio, título com só espaços
    public async Task PostTasks_TituloComApenasEspacos_Retorna400ApontandoOCampoTitle()
    {
        var response = await PostAsync(new { title = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = body.GetProperty("errors");

        errors.EnumerateObject().Should().Contain(property => property.Name.Contains("Title", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // CA-08 — 201 caracteres
    public async Task PostTasks_TituloCom201Caracteres_Retorna400()
    {
        var response = await PostAsync(new { title = new string('a', 201) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact] // CA-09 — bordas 1 e 200
    public async Task PostTasks_TituloCom1E200Caracteres_EAceito()
    {
        var respostaCurta = await PostAsync(new { title = "a" });
        var respostaLonga = await PostAsync(new { title = new string('a', 200) });

        respostaCurta.StatusCode.Should().Be(HttpStatusCode.Created);
        respostaLonga.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact] // CA-10 — descrição 2000 aceita, 2001 rejeitada
    public async Task PostTasks_DescricaoCom2000Aceita_2001Rejeitada()
    {
        var aceita = await PostAsync(new { title = "Descrição no limite", description = new string('d', 2000) });
        var rejeitada = await PostAsync(new { title = "Descrição acima do limite", description = new string('d', 2001) });

        aceita.StatusCode.Should().Be(HttpStatusCode.Created);
        rejeitada.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact] // CA-11 — dueDate no passado é aceita e marca isOverdue
    public async Task PostTasks_DueDateNoPassado_Retorna201ComIsOverdueTrue()
    {
        var response = await PostAsync(new { title = "Vencida", dueDate = "2020-01-01" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isOverdue").GetBoolean().Should().BeTrue();
    }

    [Fact] // CA-12 — dueDate igual a hoje (data do relógio, sem X-Client-Date)
    public async Task PostTasks_DueDateIgualAHojeUtc_IsOverdueFalse()
    {
        // O relógio deste teste está em 2026-08-21T00:30 UTC; sem
        // X-Client-Date o fallback é a data UTC do TimeProvider (2026-08-21).
        var response = await PostAsync(new { title = "Vence hoje", dueDate = "2026-08-21" });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isOverdue").GetBoolean().Should().BeFalse();
    }

    [Fact] // CA-12b — o cenário exato que motivou D-18
    public async Task PostTasks_ComXClientDateAnteriorAoUtc_IsOverdueUsaADataLocalDoUsuario()
    {
        // Relógio UTC em 2026-08-21T00:30 (ver InitializeAsync); usuário em
        // UTC-3 ainda está em 2026-08-20. Sem o header, isso venceria e
        // isOverdue seria true — com o header, D-18 exige false.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tasks")
        {
            Content = JsonContent.Create(new { title = "D-18", dueDate = "2026-08-20" }),
        };
        request.Headers.Add("X-User-Id", InMemoryUserLookup.ActiveUserId.ToString());
        request.Headers.Add("X-Client-Date", "2026-08-20");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isOverdue").GetBoolean().Should().BeFalse();
    }

    [Theory] // CA-13
    [InlineData("31/12/2026")]
    [InlineData("2026-13-01")]
    public async Task PostTasks_DueDateEmFormatoInvalido_Retorna400NaoNunca500(string dueDate)
    {
        var response = await PostAsync(new { title = "Data inválida", dueDate });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact] // CA-22 de BE-17 (preservado também sob o modo provisório de BE-29, CA-04)
    public async Task PostTasks_ComOwnerIdNoCorpo_EIgnorado()
    {
        var outroUsuario = Guid.NewGuid();
        var response = await PostAsync(new { title = "Tenta forjar dono", ownerId = outroUsuario });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();

        await using var context = _factory.CreateDbContext();
        var persisted = await context.Tasks.SingleAsync(task => task.Id == id);

        persisted.OwnerId.Should().Be(InMemoryUserLookup.ActiveUserId, "o dono vem do X-User-Id, nunca do corpo");
        persisted.OwnerId.Should().NotBe(outroUsuario);
    }

    private async Task<HttpResponseMessage> PostAsync(object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tasks")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-User-Id", InMemoryUserLookup.ActiveUserId.ToString());

        return await _client.SendAsync(request);
    }
}
