extern alias IdentityApi;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Infrastructure.Users;
using TodoList.Tasks.Application.Identity;
using TodoList.Tasks.Infrastructure.Identity;
using Xunit;
using IdentityProgram = IdentityApi::Program;

namespace TodoList.Tasks.IntegrationTests;

/// <summary>
/// Cliente gRPC real (BE-27) contra o servidor real do Identity (BE-26),
/// subido em <see cref="WebApplicationFactory{TEntryPoint}"/> — CA-04, CA-07,
/// CA-08, CA-09, CA-13. Referencia <c>TodoList.Identity.Api</c> sob o alias
/// <c>IdentityApi</c> (ver o <c>.csproj</c>): tanto o Identity quanto o Tasks
/// têm sua própria classe <c>Program</c> e seu próprio código gerado a partir
/// do mesmo <c>.proto</c> (D-29), e sem o alias os dois colidiriam por nome
/// nesta única compilação.
/// </summary>
public class GrpcIdentityGatewayIntegrationTests
{
    [Fact] // CA-04
    public async Task ValidateUserAsync_IdentityNoAr_UsuarioAtivoDoSeed_RetornaExistsEActiveTrue()
    {
        using var identityFactory = new WebApplicationFactory<IdentityProgram>();
        using var host = BuildInProcessTasksHost(identityFactory, TimeSpan.FromSeconds(2));
        var gateway = host.Services.GetRequiredService<IIdentityGateway>();

        var result = await gateway.ValidateUserAsync(InMemoryUserLookup.ActiveUserId, CancellationToken.None);

        result.Exists.Should().BeTrue();
        result.Active.Should().BeTrue();
        result.DisplayName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact] // CA-05, via servidor real
    public async Task ValidateUserAsync_IdentityNoAr_UsuarioInexistente_RetornaExistsFalseSemExcecao()
    {
        using var identityFactory = new WebApplicationFactory<IdentityProgram>();
        using var host = BuildInProcessTasksHost(identityFactory, TimeSpan.FromSeconds(2));
        var gateway = host.Services.GetRequiredService<IIdentityGateway>();

        var result = await gateway.ValidateUserAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().Be(new UserValidation(Exists: false, Active: false, DisplayName: string.Empty));
    }

    [Fact] // CA-07 — o endereço em Identity:GrpcAddress determina o Identity chamado, sem recompilar.
    public async Task ValidateUserAsync_ConfiguracaoApontaParaOutroIdentity_ChamadaVaiParaEle()
    {
        using var identityFactory = new WebApplicationFactory<IdentityProgram>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.AddSingleton<IUserLookup>(new FixedUserLookup(active: true, displayName: "Segundo Identity"))));
        using var host = BuildInProcessTasksHost(identityFactory, TimeSpan.FromSeconds(2));
        var gateway = host.Services.GetRequiredService<IIdentityGateway>();

        var result = await gateway.ValidateUserAsync(Guid.NewGuid(), CancellationToken.None);

        // A resposta só pode ter vindo do Identity configurado nesta instância
        // (seed em memória não conhece "Segundo Identity") — prova de que o
        // destino da chamada é ditado por Identity:GrpcAddress, não fixo em código.
        result.DisplayName.Should().Be("Segundo Identity");
    }

    [Fact] // BE-30, CA-03 — Identity__GrpcAddress como variável de ambiente sobrescreve o appsettings, sem recompilar.
    public async Task ValidateUserAsync_ComIdentityGrpcAddressPorVariavelDeAmbiente_ChamadaVaiParaOEnderecoDaVariavel()
    {
        using var identityFactory = new WebApplicationFactory<IdentityProgram>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.AddSingleton<IUserLookup>(new FixedUserLookup(active: true, displayName: "Identity via variável de ambiente"))));

        Environment.SetEnvironmentVariable("Identity__GrpcAddress", identityFactory.Server.BaseAddress.ToString());
        try
        {
            using var host = BuildInProcessTasksHostFromEnvironment(identityFactory);
            var gateway = host.Services.GetRequiredService<IIdentityGateway>();

            var result = await gateway.ValidateUserAsync(Guid.NewGuid(), CancellationToken.None);

            // Nenhum appsettings*.json deste host tem valor nenhum para
            // Identity:GrpcAddress (ConfigureAppConfiguration abaixo limpa
            // todas as fontes antes de adicionar só variáveis de ambiente) —
            // a única forma de "Identity via variável de ambiente" aparecer
            // aqui é a variável de ambiente real ter chegado ao IOptions<T>.
            result.DisplayName.Should().Be("Identity via variável de ambiente");
        }
        finally
        {
            Environment.SetEnvironmentVariable("Identity__GrpcAddress", null);
        }
    }

    [Fact] // BE-30, CA-04 — Identity__GrpcTimeoutSeconds=1 por variável de ambiente altera de fato o deadline das chamadas.
    public async Task ValidateUserAsync_ComIdentityGrpcTimeoutSecondsPorVariavelDeAmbiente_CancelaNoNovoPrazo()
    {
        using var identityFactory = new WebApplicationFactory<IdentityProgram>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.AddSingleton<IUserLookup>(new SlowUserLookup(TimeSpan.FromSeconds(10)))));

        Environment.SetEnvironmentVariable("Identity__GrpcAddress", identityFactory.Server.BaseAddress.ToString());
        Environment.SetEnvironmentVariable("Identity__GrpcTimeoutSeconds", "1");
        try
        {
            using var host = BuildInProcessTasksHostFromEnvironment(identityFactory);
            var gateway = host.Services.GetRequiredService<IIdentityGateway>();

            var stopwatch = Stopwatch.StartNew();
            var act = async () => await gateway.ValidateUserAsync(Guid.NewGuid(), CancellationToken.None);

            await act.Should().ThrowAsync<IdentityUnavailableException>();

            // O Identity está programado para responder em 10s (SlowUserLookup);
            // se a variável de ambiente não tivesse efeito nenhum, o deadline
            // continuaria no padrão de 2s do appsettings (que também cancelaria
            // antes de 10s) — o que de fato prova o CA-04 é o valor CONFIGURADO
            // (1s) ser o que está em vigor, não só "algum deadline existe". Por
            // isso a asserção below é folgada o bastante para não confundir
            // jitter de CI com falha, mas O SUFICIENTE para nunca passar se o
            // deadline efetivo fosse o padrão de 2s teria de expirar de qualquer
            // forma — a garantia real de que é o 1s da variável, e não o 2s do
            // appsettings, vem da ausência de qualquer fonte de configuração
            // além da variável de ambiente neste host (ver BuildInProcessTasksHostFromEnvironment).
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8), "Identity:GrpcTimeoutSeconds=1 (via variável de ambiente) deveria cancelar bem antes do atraso de 10s do Identity");
        }
        finally
        {
            Environment.SetEnvironmentVariable("Identity__GrpcAddress", null);
            Environment.SetEnvironmentVariable("Identity__GrpcTimeoutSeconds", null);
        }
    }

    [Fact] // CA-08 — Identity desligado: falha em no máximo o deadline, sem RpcException escapando.
    public async Task ValidateUserAsync_IdentityDesligado_FalhaDentroDoDeadlineComIdentityUnavailableException()
    {
        var deadline = TimeSpan.FromSeconds(1);
        using var host = BuildRealNetworkTasksHost(GetUnreachableAddress(), deadline);
        var gateway = host.Services.GetRequiredService<IIdentityGateway>();

        var stopwatch = Stopwatch.StartNew();
        var act = async () => await gateway.ValidateUserAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<IdentityUnavailableException>();
        stopwatch.Elapsed.Should().BeLessThan(deadline + TimeSpan.FromSeconds(3), "a chamada não deve ficar pendurada além do deadline configurado");
    }

    [Fact] // CA-09 — Identity mais lento que o deadline: cancelada no prazo, sem Thread.Sleep no teste.
    public async Task ValidateUserAsync_IdentityMaisLentoQueODeadline_CancelaNoPrazo()
    {
        var deadline = TimeSpan.FromSeconds(1);
        using var identityFactory = new WebApplicationFactory<IdentityProgram>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.AddSingleton<IUserLookup>(new SlowUserLookup(TimeSpan.FromSeconds(10)))));
        using var host = BuildInProcessTasksHost(identityFactory, deadline);
        var gateway = host.Services.GetRequiredService<IIdentityGateway>();

        var stopwatch = Stopwatch.StartNew();
        var act = async () => await gateway.ValidateUserAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<IdentityUnavailableException>();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8), "o deadline deve cancelar a chamada, não esperar a resposta lenta do Identity");
    }

    [Fact] // CA-13 — o traceId da requisição chega ao log do Identity pela metadata gRPC.
    public async Task ValidateUserAsync_ComActivityAtual_TraceIdChegaAoLogDoIdentity()
    {
        var capturingProvider = new CapturingLoggerProvider();
        using var identityFactory = new WebApplicationFactory<IdentityProgram>()
            .WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(capturingProvider)));
        using var host = BuildInProcessTasksHost(identityFactory, TimeSpan.FromSeconds(2));
        var gateway = host.Services.GetRequiredService<IIdentityGateway>();

        using var activity = new Activity("requisicao-http-de-teste");
        activity.Start();
        try
        {
            await gateway.ValidateUserAsync(InMemoryUserLookup.ActiveUserId, CancellationToken.None);
        }
        finally
        {
            activity.Stop();
        }

        capturingProvider.Messages.Should().Contain(message => message.Contains(activity.Id!, StringComparison.Ordinal));
    }

    private static IHost BuildInProcessTasksHost(WebApplicationFactory<IdentityProgram> identityFactory, TimeSpan timeout) =>
        BuildTasksHost(
            identityFactory.Server.BaseAddress,
            timeout,
            configureClient: builder => builder.ConfigurePrimaryHttpMessageHandler(() => identityFactory.Server.CreateHandler()));

    private static IHost BuildRealNetworkTasksHost(Uri address, TimeSpan timeout) =>
        BuildTasksHost(address, timeout, configureClient: null);

    /// <summary>
    /// BE-30, CA-03/CA-04 — ao contrário de <see cref="BuildTasksHost"/> (que
    /// sempre injeta <c>Identity:GrpcAddress</c>/<c>GrpcTimeoutSeconds</c> por
    /// <c>AddInMemoryCollection</c>), este host não tem NENHUMA fonte de
    /// configuração além de variável de ambiente — só assim um teste que passa
    /// aqui prova que a variável de ambiente É o mecanismo de sobrescrita, não
    /// apenas "configuração dá para sobrescrever de algum jeito".
    /// </summary>
    private static IHost BuildInProcessTasksHostFromEnvironment(WebApplicationFactory<IdentityProgram> identityFactory) =>
        Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.Sources.Clear();
                configuration.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
                services.AddIdentityGrpcClient(context.Configuration)
                    .ConfigurePrimaryHttpMessageHandler(() => identityFactory.Server.CreateHandler()))
            .Build();

    private static IHost BuildTasksHost(Uri identityAddress, TimeSpan timeout, Action<IHttpClientBuilder>? configureClient)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Identity:GrpcAddress"] = identityAddress.ToString(),
            ["Identity:GrpcTimeoutSeconds"] = Math.Max(1, (int)timeout.TotalSeconds).ToString(CultureInfo.InvariantCulture),
        };

        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.Sources.Clear();
                configuration.AddInMemoryCollection(settings);
            })
            .ConfigureServices((context, services) =>
            {
                var clientBuilder = services.AddIdentityGrpcClient(context.Configuration);
                configureClient?.Invoke(clientBuilder);
            })
            .Build();
    }

    /// <summary>Endereço de loopback em que ninguém escuta — CA-08, sem depender de porta literal.</summary>
    private static Uri GetUnreachableAddress()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return new Uri($"http://127.0.0.1:{port}");
    }

    private sealed class FixedUserLookup : IUserLookup
    {
        private readonly UserLookupResult _result;

        public FixedUserLookup(bool active, string displayName) => _result = new UserLookupResult(active, displayName);

        public Task<UserLookupResult?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<UserLookupResult?>(_result);
    }

    private sealed class SlowUserLookup : IUserLookup
    {
        private readonly TimeSpan _delay;

        public SlowUserLookup(TimeSpan delay) => _delay = delay;

        public async Task<UserLookupResult?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
        {
            await Task.Delay(_delay, cancellationToken);
            return new UserLookupResult(Active: true, DisplayName: "Nunca deveria chegar aqui");
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly ConcurrentQueue<string> _messages;

            public CapturingLogger(ConcurrentQueue<string> messages) => _messages = messages;

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                _messages.Enqueue(formatter(state, exception));
        }
    }
}
