extern alias IdentityApi;

using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Testcontainers.PostgreSql;
using TodoList.Contracts.Identity.V1;
using TodoList.Identity.Infrastructure.Persistence;
using Xunit;
using IdentityProgram = IdentityApi::Program;
using InfrastructurePersistence = TodoList.Identity.Infrastructure.Persistence.ServiceCollectionExtensions;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>
/// Gateway REAL + Identity REAL (Postgres via Testcontainers), ambos em processo, ligados por gRPC em memória
/// (BE-24). O Tasks fica de fora — nenhum cenário de log aqui o alcança. Cada host escreve seus eventos num
/// <see cref="JsonLogSink"/>: o mesmo JSON que iria para o console, com o nível mínimo em <c>Verbose</c> (inclusive
/// EF Core, ASP.NET Core, gRPC e HttpClient) — o teste vê o pior caso de verbosidade. <b>Requer Docker.</b>
/// </summary>
public sealed class RealChainFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("todolist")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly List<string> _tempFiles = [];

    private WebApplicationFactory<IdentityProgram>? _identity;
    private WebApplicationFactory<Program>? _gateway;

    public JsonLogSink GatewayLog { get; } = new();

    public JsonLogSink IdentityLog { get; } = new();

    /// <summary>Todo o log dos dois serviços, linha a linha (JSON).</summary>
    public IReadOnlyList<string> AllLogLines => [.. GatewayLog.Lines, .. IdentityLog.Lines];

    public HttpClient CreateGatewayClient() =>
        _gateway!.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    /// <summary>Lê, direto do banco, o hash de senha gravado (<c>identity.users.password_hash</c>).</summary>
    public async Task<string> ReadPasswordHashAsync(string email)
    {
        await using var context = CreateContext();

        return await context.Database
            .SqlQuery<string>($"select password_hash as \"Value\" from identity.users where email = {email}")
            .SingleAsync();
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        using var rsa = RSA.Create(2048);
        var privateKeyPath = WriteTempFile("identity-private", rsa.ExportPkcs8PrivateKeyPem());
        var publicKeyPath = WriteTempFile("gateway-public", rsa.ExportSubjectPublicKeyInfoPem());

        _identity = new WebApplicationFactory<IdentityProgram>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(VerboseLogging(new Dictionary<string, string?>
                {
                    [$"ConnectionStrings:{InfrastructurePersistence.ConnectionStringName}"] = _postgres.GetConnectionString(),
                    ["UserStore:Provider"] = "Persisted",
                    ["Jwt:PrivateKeyPath"] = privateKeyPath,
                    ["PasswordHashing:Iterations"] = "1000",
                    ["Retention:Enabled"] = "false",
                })));
            builder.ConfigureServices(services => services.AddSingleton<ILogEventSink>(IdentityLog));
        });

        var identityHandler = _identity.Server.CreateHandler();

        _gateway = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Production: o 500 genérico (sem a mensagem da exceção) é o comportamento de produção (CA-06).
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(VerboseLogging(new Dictionary<string, string?>
                {
                    ["Backends:IdentityGrpcAddress"] = "http://identity",
                    ["Jwt:PublicKeyPath"] = publicKeyPath,
                    ["RefreshCookie:Secure"] = "false",
                })));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ILogEventSink>(GatewayLog);

                // CA-06: o Tasks "de verdade" aqui é um backend que estoura — nenhum cenário do Gateway real o alcança além deste.
                services.AddScoped<TodoList.Gateway.Api.Backends.ITasksBackend, ThrowingTasksBackend>();

                // Mesma técnica de GatewayApiFactory: o cliente de produção ganha só o handler em memória do Identity real.
                services.AddGrpcClient<IdentityService.IdentityServiceClient>(options => options.Address = new Uri("http://identity"))
                    .ConfigurePrimaryHttpMessageHandler(() => identityHandler);
            });
        });

        // Sobe os dois hosts agora, para a configuração valer desde a primeira requisição.
        _ = _identity.Server;
        _ = _gateway.Server;
    }

    public async Task DisposeAsync()
    {
        _gateway?.Dispose();
        _identity?.Dispose();
        await _postgres.DisposeAsync();

        foreach (var file in _tempFiles)
        {
            File.Delete(file);
        }
    }

    private static Dictionary<string, string?> VerboseLogging(Dictionary<string, string?> settings)
    {
        settings["Serilog:MinimumLevel:Default"] = "Verbose";
        settings["Serilog:MinimumLevel:Override:Microsoft"] = "Verbose";
        settings["Serilog:MinimumLevel:Override:System"] = "Verbose";
        settings["Serilog:MinimumLevel:Override:System.Net.Http.HttpClient"] = "Verbose";

        return settings;
    }

    private string WriteTempFile(string name, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"log-leakage-{name}-{Guid.NewGuid():N}.pem");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);

        return path;
    }

    private IdentityDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                _postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsHistoryTable(IdentityDbContext.MigrationsHistoryTableName, IdentityDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options);
}

/// <summary>Sink do Serilog que guarda cada evento como a linha JSON que o console receberia.</summary>
public sealed class JsonLogSink : ILogEventSink
{
    private readonly RenderedCompactJsonFormatter _formatter = new();
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
            {
                return [.. _lines];
            }
        }
    }

    public void Emit(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        _formatter.Format(logEvent, writer);

        lock (_lines)
        {
            _lines.Add(writer.ToString());
        }
    }
}

/// <summary>
/// <see cref="TodoList.Gateway.Api.Backends.ITasksBackend"/> que lança uma exceção inesperada em toda chamada — o
/// "bug de verdade" do cenário CA-06 (exceção não tratada: 500 genérico para o cliente, stack trace só no log).
/// </summary>
internal sealed class ThrowingTasksBackend : TodoList.Gateway.Api.Backends.ITasksBackend
{
    public const string SecretDetail = "detalhe-interno-que-nao-pode-vazar";

    public Task<TodoList.Gateway.Api.Contracts.TaskHttpResponse> CreateTaskAsync(TodoList.Gateway.Api.Contracts.CreateTaskHttpRequest request, CancellationToken cancellationToken) => throw Boom();

    public Task<TodoList.Gateway.Api.Contracts.ListTasksHttpResponse> ListTasksAsync(TodoList.Gateway.Api.Contracts.ListTasksHttpRequest request, CancellationToken cancellationToken) => throw Boom();

    public Task<TodoList.Gateway.Api.Contracts.TaskHttpResponse> GetTaskAsync(string id, CancellationToken cancellationToken) => throw Boom();

    public Task<TodoList.Gateway.Api.Contracts.TaskHttpResponse> UpdateTaskAsync(string id, TodoList.Gateway.Api.Contracts.UpdateTaskHttpRequest request, CancellationToken cancellationToken) => throw Boom();

    public Task<TodoList.Gateway.Api.Contracts.TaskHttpResponse> CompleteTaskAsync(string id, CancellationToken cancellationToken) => throw Boom();

    public Task<TodoList.Gateway.Api.Contracts.TaskHttpResponse> ReopenTaskAsync(string id, CancellationToken cancellationToken) => throw Boom();

    public Task DeleteTaskAsync(string id, CancellationToken cancellationToken) => throw Boom();

    private static InvalidOperationException Boom() => new(SecretDetail);
}
