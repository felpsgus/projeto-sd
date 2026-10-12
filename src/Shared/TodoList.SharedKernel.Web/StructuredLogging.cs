using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace TodoList.SharedKernel.Web;

/// <summary>
/// Log estruturado em JSON no console (BE-24), igual nos três serviços. Este arquivo é
/// compilado também no Gateway (<c>Compile Include</c> com link, sem <c>ProjectReference</c>):
/// o Gateway não referencia o SharedKernel (D-33), mas não deve ter uma segunda cópia do código.
/// </summary>
public static class StructuredLogging
{
    /// <summary>Metadata gRPC com o usuário autenticado que o Gateway repassa ao Tasks (D-34).</summary>
    public const string CallerUserIdHeader = "x-user-id";

    /// <summary>
    /// Troca o logging padrão pelo Serilog: JSON compacto renderizado no console, níveis em
    /// <c>Serilog:MinimumLevel</c> (sobrescrevível por <c>Serilog__MinimumLevel__Default</c>),
    /// e <c>service</c>/<c>environment</c>/<c>version</c>/<c>traceId</c>/<c>spanId</c> em toda entrada.
    /// Sinks registrados no DI (<see cref="ILogEventSink"/>) também recebem os eventos — é o gancho dos testes.
    /// </summary>
    public static WebApplicationBuilder AddStructuredLogging(this WebApplicationBuilder builder, string service)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

        // preserveStaticLogger: cada host usa o próprio logger (sem tocar em Log.Logger) — vários hosts no
        // mesmo processo (testes de integração) não misturam sinks nem níveis.
        builder.Host.UseSerilog(
            (context, services, logger) => logger
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.With<ActivityEnricher>()
            .Enrich.WithProperty("service", service)
            .Enrich.WithProperty("environment", context.HostingEnvironment.EnvironmentName)
            .Enrich.WithProperty("version", version)
            .WriteTo.Console(new RenderedCompactJsonFormatter()),
            preserveStaticLogger: true);

        return builder;
    }

    /// <summary>
    /// Uma entrada por requisição (método, caminho, status, duração). Nunca corpo nem headers.
    /// <paramref name="userId"/> devolve o usuário autenticado ou <c>null</c> — anônimo não ganha o campo.
    /// </summary>
    public static IApplicationBuilder UseStructuredRequestLogging(this IApplicationBuilder app, Func<HttpContext, string?> userId) =>
        app.UseSerilogRequestLogging(options =>
        {
            // O padrão seria o Log.Logger estático, que preserveStaticLogger deixa de fora: usa o logger do host.
            options.Logger = app.ApplicationServices.GetRequiredService<Serilog.ILogger>();
            options.EnrichDiagnosticContext = (diagnostics, http) =>
            {
                var id = userId(http);
                if (!string.IsNullOrEmpty(id))
                {
                    diagnostics.Set("userId", id);
                }
            };
        });

    /// <summary>Usuário repassado pelo Gateway na metadata gRPC (Identity e Tasks só aceitam chamadas internas).</summary>
    public static string? CallerUserId(HttpContext http) => http.Request.Headers[CallerUserIdHeader].FirstOrDefault();

    /// <summary>
    /// <c>traceId</c> W3C da requisição — o mesmo valor do log de requisição e do <c>ProblemDetails</c>.
    /// Sem <see cref="Activity"/> (ninguém ouvindo), cai no identificador do ASP.NET Core.
    /// </summary>
    public static string GetTraceId(this HttpContext http) => Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier;

    private sealed class ActivityEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            if (Activity.Current is { } activity)
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("traceId", activity.TraceId.ToString()));
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("spanId", activity.SpanId.ToString()));
            }
        }
    }
}
