using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TodoList.SharedKernel;
using TodoList.Tasks.Api.ErrorHandling;
using TodoList.Tasks.Api.ResultMapping;
using TodoList.Tasks.Api.Validation;
using Xunit;

namespace TodoList.Tasks.IntegrationTests;

/// <summary>
/// CA-03, CA-04, CA-05, CA-06, CA-08 e CA-09 de BE-03, exercitados sobre um
/// host mínimo criado neste projeto de teste — não existem endpoints de
/// negócio ainda, e nenhuma rota de exemplo entra na aplicação de produção
/// (ver notas do BE-03). O host reaproveita exatamente a fiação de produção
/// (<see cref="ApiErrorHandlingExtensions"/>, <see cref="ResultHttpResults"/>,
/// <see cref="ValidationFilterExtensions"/>), então o que é verificado aqui é
/// o mesmo mecanismo usado pelo <c>Program.cs</c> real.
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

    [Fact] // CA-04
    public async Task Endpoint_ComRequestInvalido_Retorna400ComErrosPorCampo()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(new Uri("/test/validate", UriKind.Relative), new ExampleRequest(string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = body.GetProperty("errors");

        errors.GetProperty(nameof(ExampleRequest.Name)).EnumerateArray().Should().NotBeEmpty(
            "CA-04 exige a lista de erros por campo, não uma mensagem única concatenada");
        body.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact] // CA-05, CA-06
    public async Task Endpoint_ComExcecaoNaoTratada_Retorna500GenericoENaoVazaDetalheForaDeDevelopment()
    {
        var capturingProvider = new CapturingLoggerProvider();
        using var host = await CreateHostAsync(environment: "Production", capturingProvider);
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
        capturingProvider.Messages.Should().Contain(message => message.Contains(traceId!, StringComparison.Ordinal));
    }

    [Fact] // CA-09 (metade Tasks — a outra metade é TodoList.Identity.IntegrationTests)
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

    private static async Task<IHost> CreateHostAsync(string environment = "Production", CapturingLoggerProvider? loggerProvider = null)
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHostBuilder =>
            {
                webHostBuilder
                    .UseTestServer()
                    .UseEnvironment(environment)
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddApiErrorHandling();
                        services.AddValidatorsFromAssemblyContaining<ExampleRequestValidator>();

                        if (loggerProvider is not null)
                        {
                            services.AddLogging(logging => logging.AddProvider(loggerProvider));
                        }
                    })
                    .Configure(app =>
                    {
                        app.UseApiErrorHandling();
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapPost("/test/validate", (ExampleRequest request) => Microsoft.AspNetCore.Http.Results.Ok())
                                .WithRequestValidation<ExampleRequest>();

                            endpoints.MapGet("/test/not-found", () =>
                                Result.Failure(new Error("test.not_found", "Recurso de teste não encontrado.", ErrorType.NotFound))
                                    .ToHttpResult());

                            Func<Microsoft.AspNetCore.Http.IResult> boom = () =>
                                throw new InvalidOperationException("detalhe interno que não pode vazar para o cliente");
                            endpoints.MapGet("/test/boom", boom);
                        });
                    });
            });

        var host = hostBuilder.Build();
        await host.StartAsync();

        return host;
    }

    public sealed record ExampleRequest(string Name);

    public sealed class ExampleRequestValidator : AbstractValidator<ExampleRequest>
    {
        public ExampleRequestValidator()
        {
            RuleFor(request => request.Name).NotEmpty();
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly List<string> _messages;

            public CapturingLogger(List<string> messages)
            {
                _messages = messages;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _messages.Add(formatter(state, exception));
            }
        }
    }
}
