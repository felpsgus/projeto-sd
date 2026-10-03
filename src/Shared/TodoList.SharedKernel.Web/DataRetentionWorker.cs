using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TodoList.SharedKernel.Retention;

namespace TodoList.SharedKernel.Web;

/// <summary>
/// Expurgo periódico in-process (BE-23, D-13): um ciclo ao subir e outro a cada
/// <c>Retention:IntervalHours</c>. Falha de ciclo é logada e o próximo tenta de novo (CA-08).
/// Várias instâncias podem rodar em paralelo: o DELETE é idempotente, sem lock distribuído.
/// </summary>
public sealed partial class DataRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly RetentionOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<DataRetentionWorker> _logger;

    public DataRetentionWorker(
        IServiceScopeFactory scopes, IOptions<RetentionOptions> options, TimeProvider time, ILogger<DataRetentionWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(_options.IntervalHours), _time);

        try
        {
            do
            {
                await RunCycleAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown (CA-10): nenhuma transação fica aberta, cada lote é um DELETE atômico.
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            using var scope = _scopes.CreateScope();
            var purger = scope.ServiceProvider.GetRequiredService<IRetentionPurger>();
            var counts = await purger.PurgeAsync(cancellationToken);

            var summary = string.Join(", ", counts.Select(pair => $"{pair.Key}={pair.Value}"));
            var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            LogCycleCompleted(summary, elapsedMs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogCycleFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Expurgo concluído: removidos [{Counts}] em {DurationMs} ms.")]
    private partial void LogCycleCompleted(string counts, long durationMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha no ciclo de expurgo; o próximo ciclo tenta de novo.")]
    private partial void LogCycleFailed(Exception exception);
}

public static class DataRetentionServiceCollectionExtensions
{
    /// <summary>Registra <c>RetentionOptions</c> (validado no start), o purger do serviço e o worker.</summary>
    public static IServiceCollection AddDataRetention<TPurger>(this IServiceCollection services, IConfiguration configuration)
        where TPurger : class, IRetentionPurger
    {
        services
            .AddOptions<RetentionOptions>()
            .Bind(configuration.GetSection(RetentionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IRetentionPurger, TPurger>();
        services.AddHostedService<DataRetentionWorker>();

        return services;
    }
}
