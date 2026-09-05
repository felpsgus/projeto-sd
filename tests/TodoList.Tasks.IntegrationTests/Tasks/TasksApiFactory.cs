extern alias IdentityApi;

using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TodoList.Contracts.Identity.V1;
using TodoList.Tasks.Infrastructure.Persistence;
using TodoList.Tasks.Infrastructure.Persistence.Interceptors;
using IdentityProgram = IdentityApi::Program;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> do Tasks Api real (mesmo
/// pipeline de produção: <c>Program.cs</c>, filtros, <c>ResultHttpResults</c>,
/// <c>GlobalExceptionHandler</c>) usado pelos testes de <c>POST /api/tasks</c>
/// (BE-17, BE-28, BE-29). Duas substituições de infraestrutura, nada de
/// negócio:
/// <list type="bullet">
/// <item><see cref="TasksDbContext"/> passa a apontar para SQLite in-memory
/// em vez de Postgres real — mesmo padrão de
/// <c>TodoTaskSqlitePersistenceTests</c> (BE-05): exercita o pipeline
/// relacional real (interceptors, filtro de soft delete) sem Docker;</item>
/// <item><see cref="TimeProvider"/> vira um <see cref="FakeTimeProvider"/>
/// controlável pelo teste (CA-07, CA-11, CA-12 de BE-17 dependem de um
/// relógio determinístico).</item>
/// </list>
/// O destino do cliente gRPC do Identity (<c>Identity:GrpcAddress</c>) é
/// sempre lido de configuração — nunca endereço literal em código de
/// produção (CA-06 de BE-27) — e aqui é apontado ou para um
/// <see cref="WebApplicationFactory{TEntryPoint}"/> do Identity real
/// (roteado em memória via <see cref="WebApplicationFactory{TEntryPoint}.Server"/>,
/// mesma técnica de <c>GrpcIdentityGatewayIntegrationTests</c>) ou para um
/// endereço morto de rede real (cenário de indisponibilidade, BE-28 CA-08).
/// </summary>
internal sealed class TasksApiFactory : WebApplicationFactory<Program>
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly FakeTimeProvider _timeProvider;
    private readonly Uri _identityAddress;
    private readonly WebApplicationFactory<IdentityProgram>? _identityFactory;
    private readonly IReadOnlyDictionary<string, string?> _configOverrides;
    private readonly Action<IServiceCollection>? _configureServices;

    /// <param name="connection">Conexão SQLite já aberta — o teste controla o ciclo de vida.</param>
    /// <param name="timeProvider">Relógio fake compartilhado com o teste (CA-07, CA-11 de BE-17).</param>
    /// <param name="identityFactory">
    /// Quando informado, o cliente gRPC do Identity é roteado, em memória, para
    /// este host real do Identity (BE-28: caminho de sucesso/rejeição). Quando
    /// <c>null</c>, o cliente fala com <paramref name="identityAddress"/> pela
    /// rede de verdade — usado só para o cenário de indisponibilidade.
    /// </param>
    /// <param name="identityAddress">
    /// Endereço configurado em <c>Identity:GrpcAddress</c>. Com
    /// <paramref name="identityFactory"/> informado, só precisa ser um
    /// <see cref="Uri"/> sintaticamente válido (o tráfego real vai pelo
    /// handler em memória, não por esta rede); sem ele, é o endereço morto
    /// usado no teste de indisponibilidade.
    /// </param>
    /// <param name="configOverrides">Chaves de configuração adicionais (ex.: <c>Tasks:MaxActivePerUser</c>, <c>Tasks:AllowAnonymousCreate</c>).</param>
    /// <param name="configureServices">
    /// Gancho extra de DI, aplicado por último (depois da substituição de
    /// banco/relógio/gRPC acima) — usado quando um teste precisa de algo que
    /// não dá para expressar como string de configuração (ex.: forçar
    /// <c>TaskOptions.MaxActivePerUser = null</c> via <c>PostConfigure</c>,
    /// CA-20 de BE-17) sem perder os métodos auxiliares desta classe, que
    /// <see cref="WebApplicationFactory{TEntryPoint}.WithWebHostBuilder"/>
    /// perderia (devolve o tipo base).
    /// </param>
    public TasksApiFactory(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        FakeTimeProvider timeProvider,
        WebApplicationFactory<IdentityProgram>? identityFactory,
        Uri identityAddress,
        IReadOnlyDictionary<string, string?>? configOverrides = null,
        Action<IServiceCollection>? configureServices = null)
    {
        _connection = connection;
        _timeProvider = timeProvider;
        _identityFactory = identityFactory;
        _identityAddress = identityAddress;
        _configOverrides = configOverrides ?? new Dictionary<string, string?>();
        _configureServices = configureServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Development" só para que uma falha inesperada (bug real, não
        // cenário de teste) traga o detalhe da exceção no corpo do 500 em
        // vez do genérico de produção — mais fácil de diagnosticar; BE-03
        // CA-05 (produção não vaza detalhe) é coberto à parte, em
        // ErrorHandlingTests, que testa isso explicitamente sob "Production".
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>(_configOverrides)
            {
                ["Identity:GrpcAddress"] = _identityAddress.ToString(),
                ["Service:DisplayName"] = "Tasks Service (teste de integração)",
                [$"ConnectionStrings:{ServiceCollectionExtensions.ConnectionStringName}"] = "DataSource=nao-usado-sqlite-substitui",
            };

            settings.TryAdd("Identity:GrpcTimeoutSeconds", "2");

            configuration.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            RemoveDescriptor<DbContextOptions<TasksDbContext>>(services);
            RemoveDescriptor<TasksDbContext>(services);

            // Não usa AddDbContext(...) aqui de propósito: AddTasksPersistence
            // (chamado dentro do Program.cs real, antes deste ConfigureServices
            // rodar) já registrou os serviços internos do provider Npgsql no
            // IServiceCollection do host. Um segundo AddDbContext<TasksDbContext>
            // com UseSqlite adicionaria os serviços do provider Sqlite ao MESMO
            // IServiceCollection — e o EF Core recusa resolver um DbContext com
            // dois providers de banco registrados ("Only a single database
            // provider can be registered"). DbContextOptions construídas à mão
            // (mesmo padrão de CreateDbContext(), abaixo) usam o provider
            // resolvido internamente pelo próprio EF Core para aquela instância
            // de options, sem tocar no IServiceCollection do host — nenhum
            // conflito.
            services.AddScoped(_ => CreateDbContext());

            RemoveDescriptor<TimeProvider>(services);
            services.AddSingleton<TimeProvider>(_timeProvider);

            if (_identityFactory is not null)
            {
                // Mesma técnica de GrpcIdentityGatewayIntegrationTests: o
                // cliente gRPC típico registrado por AddIdentityGrpcClient
                // (chamado dentro do Program.cs real) fala HTTP/2 com um
                // handler que, em vez de abrir socket, entrega a chamada
                // direto ao TestServer do Identity — sem rede real, sem
                // porta literal em lugar nenhum.
                services.AddGrpcClient<IdentityService.IdentityServiceClient>(options => options.Address = _identityAddress)
                    .ConfigurePrimaryHttpMessageHandler(() => _identityFactory.Server.CreateHandler());
            }

            _configureServices?.Invoke(services);
        });
    }

    /// <summary>Cria o schema no banco SQLite in-memory da conexão deste factory.</summary>
    public async Task EnsureDatabaseCreatedAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        await context.Database.EnsureCreatedAsync();
    }

    /// <summary>Novo <see cref="TasksDbContext"/> apontando para o mesmo banco do factory — para arranjo/asserção direta no teste.</summary>
    public TasksDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditingSaveChangesInterceptor(_timeProvider))
            .Options;

        return new TasksDbContext(options);
    }

    private static void RemoveDescriptor<TService>(IServiceCollection services)
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(TService));

        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }
    }

    /// <summary>Endereço de loopback em que ninguém escuta — usado no cenário de indisponibilidade (BE-28, CA-08).</summary>
    public static Uri GetUnreachableAddress()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return new Uri($"http://127.0.0.1:{port}");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
