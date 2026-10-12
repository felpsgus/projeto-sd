using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TodoList.SharedKernel.Retention;
using TodoList.SharedKernel.Web;
using Xunit;

namespace TodoList.Tasks.UnitTests.Retention;

/// <summary>
/// BE-23 (CA-08 a CA-11): o worker é exercitado com <see cref="FakeTimeProvider"/> — o relógio
/// avança, nunca se espera tempo real (os <c>WaitAsync</c> abaixo são só guarda contra travamento).
/// </summary>
public sealed class DataRetentionWorkerTests : IDisposable
{
    private static readonly TimeSpan _guard = TimeSpan.FromSeconds(10);

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-03T10:00:00Z"));
    private readonly ListLogger _logger = new();
    private readonly SemaphoreSlim _calls = new(0);

    public void Dispose() => _calls.Dispose();

    [Fact] // CA-08
    public async Task Ciclo_QuandoOPurgerLanca_LogaErroENaoDerrubaEOProximoCicloRoda()
    {
        var attempt = 0;
        var worker = CreateWorker(new RetentionOptions(), _ =>
        {
            _calls.Release();

            return ++attempt == 1
                ? throw new InvalidOperationException("banco fora")
                : Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int> { ["tasks"] = 0 });
        });

        await worker.StartAsync(CancellationToken.None);
        await _calls.WaitAsync(_guard);
        _time.Advance(TimeSpan.FromHours(24));
        await _calls.WaitAsync(_guard);

        worker.ExecuteTask!.IsCompleted.Should().BeFalse("o worker segue vivo depois do erro");
        _logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Error);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact] // CA-09
    public async Task Ciclo_ComRetentionDesligado_NaoChamaOPurger()
    {
        var worker = CreateWorker(new RetentionOptions { Enabled = false }, _ =>
        {
            _calls.Release();

            return Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());
        });

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(_guard);
        _time.Advance(TimeSpan.FromHours(48));

        _calls.CurrentCount.Should().Be(0);
    }

    [Fact] // CA-10
    public async Task Shutdown_ComPurgerEmAndamento_CancelaSemErroENemLogDeFalha()
    {
        var worker = CreateWorker(new RetentionOptions(), async ct =>
        {
            _calls.Release();
            await Task.Delay(Timeout.Infinite, ct);

            return new Dictionary<string, int>();
        });

        await worker.StartAsync(CancellationToken.None);
        await _calls.WaitAsync(_guard);
        await worker.StopAsync(CancellationToken.None);

        worker.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        _logger.Entries.Should().NotContain(entry => entry.Level == LogLevel.Error);
    }

    [Fact] // CA-11
    public async Task Ciclo_QuandoConclui_LogaContagensPorTipoEADuracao()
    {
        var worker = CreateWorker(new RetentionOptions(), _ =>
        {
            _calls.Release();

            return Task.FromResult<IReadOnlyDictionary<string, int>>(
                new Dictionary<string, int> { ["tasks"] = 3, ["refresh_tokens"] = 7 });
        });

        await worker.StartAsync(CancellationToken.None);
        await _calls.WaitAsync(_guard);
        await worker.StopAsync(CancellationToken.None);

        _logger.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Information
            && entry.Message.Contains("tasks=3", StringComparison.Ordinal)
            && entry.Message.Contains("refresh_tokens=7", StringComparison.Ordinal)
            && entry.Message.Contains(" ms", StringComparison.Ordinal));
    }

    private DataRetentionWorker CreateWorker(
        RetentionOptions options, Func<CancellationToken, Task<IReadOnlyDictionary<string, int>>> purge)
    {
        var services = new ServiceCollection()
            .AddScoped<IRetentionPurger>(_ => new DelegatePurger(purge))
            .BuildServiceProvider();

        return new DataRetentionWorker(
            services.GetRequiredService<IServiceScopeFactory>(), Options.Create(options), _time, _logger);
    }

    private sealed class DelegatePurger(Func<CancellationToken, Task<IReadOnlyDictionary<string, int>>> purge) : IRetentionPurger
    {
        public Task<IReadOnlyDictionary<string, int>> PurgeAsync(CancellationToken cancellationToken) => purge(cancellationToken);
    }

    private sealed class ListLogger : ILogger<DataRetentionWorker>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
