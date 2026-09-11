// TodoList.Gateway.Api gera só o lado CLIENTE dos dois .proto; o projeto
// TodoList.Gateway.IntegrationTests.Fakes gera o lado SERVIDOR dos mesmos
// .proto para hospedar os dublês (BE-36) — ambos definem os mesmos tipos de
// mensagem no mesmo `csharp_namespace`, então esta única referência precisa
// de alias para não colidir por nome (mesma técnica de
// TodoList.Tasks.IntegrationTests com `extern alias IdentityApi`). Nenhum
// tipo gerado pelo proto é nomeado aqui: só os POCOs de Fakes
// (<c>FakeIdentityService</c>, <c>FakeTasksService</c>, <c>FakeGrpcHost&lt;&gt;</c>).
extern alias Fakes;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TodoList.Contracts.Identity.V1;
using TodoList.Contracts.Tasks.V1;
using FakeIdentityService = Fakes::TodoList.Gateway.IntegrationTests.Fakes.FakeIdentityService;
using FakeIdentityServiceHost = Fakes::TodoList.Gateway.IntegrationTests.Fakes.FakeGrpcHost<Fakes::TodoList.Gateway.IntegrationTests.Fakes.FakeIdentityService>;
using FakeTasksService = Fakes::TodoList.Gateway.IntegrationTests.Fakes.FakeTasksService;
using FakeTasksServiceHost = Fakes::TodoList.Gateway.IntegrationTests.Fakes.FakeGrpcHost<Fakes::TodoList.Gateway.IntegrationTests.Fakes.FakeTasksService>;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> do Gateway real (mesmo
/// pipeline de produção: <c>Program.cs</c>, autenticação, validação,
/// <c>GlobalExceptionHandler</c>) — os dois clientes gRPC de produção
/// (registrados por <c>AddBackendGrpcClients</c> dentro do <c>Program.cs</c>
/// real) têm o <b>handler HTTP primário</b> trocado pelo handler em memória
/// dos servidores gRPC falsos (<see cref="FakeIdentityService"/>,
/// <see cref="FakeTasksService"/>) — nunca as interfaces
/// <c>IIdentityBackend</c>/<c>ITasksBackend</c>, para que o interceptor de
/// cliente, os deadlines e os trailers sejam exercitados de verdade (BE-36,
/// abordagem obrigatória de teste).
/// </summary>
public sealed class GatewayApiFactory : WebApplicationFactory<Program>
{
    public FakeIdentityService Identity { get; } = new();

    public FakeTasksService Tasks { get; } = new();

    /// <summary>Mensagens de log capturadas do host (CA-26: prova de que nenhuma carrega token/senha/corpo).</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    private readonly FakeIdentityServiceHost _identityHost;
    private readonly FakeTasksServiceHost _tasksHost;

    /// <summary>Construtor sem parâmetros — exigido pelo <c>IClassFixture&lt;&gt;</c> do xUnit.</summary>
    public GatewayApiFactory()
    {
        _identityHost = new FakeIdentityServiceHost(Identity);
        _tasksHost = new FakeTasksServiceHost(Tasks);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Development" só para expor a documentação OpenAPI/Scalar (CA-14) —
        // uma exceção inesperada continua passando pelo GlobalExceptionHandler
        // igual em produção.
        builder.UseEnvironment("Development");

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Backends:IdentityGrpcAddress"] = "http://fake-identity",
                ["Backends:TasksGrpcAddress"] = "http://fake-tasks",
                ["Backends:IdentityGrpcTimeoutSeconds"] = "2",
                ["Backends:TasksGrpcTimeoutSeconds"] = "5",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Mesma técnica de TasksApiFactory (BE-35): um segundo
            // AddGrpcClient para o MESMO tipo de cliente não substitui o
            // registro de produção — acrescenta configuração ao mesmo nome
            // (o full name do tipo), então o endereço e o handler primário
            // aqui vencem, mas os interceptors já registrados em produção
            // (ClientMetadataInterceptor, só no cliente do Tasks) continuam
            // valendo.
            services.AddGrpcClient<IdentityService.IdentityServiceClient>(options => options.Address = new Uri("http://fake-identity"))
                .ConfigurePrimaryHttpMessageHandler(() => _identityHost.CreateHandler());

            services.AddGrpcClient<TasksService.TasksServiceClient>(options => options.Address = new Uri("http://fake-tasks"))
                .ConfigurePrimaryHttpMessageHandler(() => _tasksHost.CreateHandler());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _identityHost.Dispose();
            _tasksHost.Dispose();
        }
    }
}
