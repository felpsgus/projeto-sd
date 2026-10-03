using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog.Core;
using Serilog.Events;
using TodoList.Identity.Api.ErrorHandling;
using TodoList.SharedKernel.Web;
using Xunit;

namespace TodoList.Identity.IntegrationTests;

/// <summary>
/// CA-03, CA-05, CA-06, CA-08 e CA-09 de BE-03, exercitados sobre um
/// host mínimo criado neste projeto de teste — não existem endpoints de
/// negócio ainda, e nenhuma rota de exemplo entra na aplicação de produção
/// (ver notas do BE-03). O host replica a fiação de produção do <c>Program.cs</c>
/// (<see cref="GlobalExceptionHandler"/> + <c>ProblemDetails</c> com <c>traceId</c>), então o que é verificado aqui é
/// o mesmo mecanismo.
/// </summary>
public class ErrorHandlingTests
{
    [Fact] // CA-08, CA-03
    public async Task Endpoint_ComResultDeFalhaNotFound_Responde404SemSwitchManual()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync(new Uri("/test/not-found", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("type").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("status").GetInt32().Should().Be(404);
        body.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact] // CA-05, CA-06
    public async Task Endpoint_ComExcecaoNaoTratada_Retorna500GenericoENaoVazaDetalheForaDeDevelopment()
    {
        var sink = new EventSink();
        using var host = await CreateHostAsync(environment: "Production", sink);
        var client = host.GetTestClient();

        var response = await client.GetAsync(new Uri("/test/boom", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var detail = body.GetProperty("detail").GetString();

        detail.Should().NotContain("InvalidOperationException");
        detail.Should().NotContain("detalhe interno");
        detail.Should().NotContain("at TodoList");

        var traceId = body.GetProperty("traceId").GetString();
        traceId.Should().NotBeNullOrWhiteSpace();

        // CA-06: o traceId da resposta corresponde ao da entrada de log da exceção.
        sink.Events.Should().Contain(entry =>
            entry.Exception is InvalidOperationException && entry.Properties["traceId"].ToString() == $"\"{traceId}\"");
    }

    [Fact] // CA-09 (metade Identity — a outra metade é TodoList.Tasks.IntegrationTests)
    public async Task ProblemDetails_TemAMesmaFormaParaOMesmoErrorType()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync(new Uri("/test/not-found", UriKind.Relative));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var camposEsperados = new[] { "type", "title", "status", "detail", "traceId" };

        foreach (var campo in camposEsperados)
        {
            body.TryGetProperty(campo, out _).Should().BeTrue($"ProblemDetails precisa do campo '{campo}' (CA-03, CA-09)");
        }
    }

    private static async Task<IHost> CreateHostAsync(string environment = "Production", ILogEventSink? sink = null)
    {
        // Mesma fiação de produção, incluindo o enricher de traceId do log estruturado.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.AddStructuredLogging("test");
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails =
            context => context.ProblemDetails.Extensions["traceId"] = context.HttpContext.GetTraceId());

        if (sink is not null)
        {
            builder.Services.AddSingleton(sink);
        }

        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapGet("/test/not-found", () =>
            Microsoft.AspNetCore.Http.Results.Problem(detail: "Recurso de teste não encontrado.", statusCode: 404));

        Func<Microsoft.AspNetCore.Http.IResult> boom = () =>
            throw new InvalidOperationException("detalhe interno que não pode vazar para o cliente");
        app.MapGet("/test/boom", boom);

        await app.StartAsync();

        return app;
    }

    private sealed class EventSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
}
