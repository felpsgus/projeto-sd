using Microsoft.Extensions.Options;
using TodoList.Contracts.Identity.V1;
using TodoList.Contracts.Tasks.V1;
using TodoList.Gateway.Api.Configuration;

namespace TodoList.Gateway.Api.Backends;

/// <summary>
/// Fiação dos dois clientes gRPC tipados e das abstrações
/// <see cref="IIdentityBackend"/>/<see cref="ITasksBackend"/> (BE-36) — mesmo
/// padrão de <c>TodoList.Tasks.Infrastructure.Identity.ServiceCollectionExtensions</c>:
/// <c>AddGrpcClient</c>, nunca <c>GrpcChannel.ForAddress</c> num método de
/// chamada.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBackendGrpcClients(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<BackendOptions>()
            .Bind(configuration.GetSection(BackendOptions.SectionName))
            .ValidateDataAnnotations()
            // CA-23: [Required] só pega ausência/vazio — uma string presente
            // mas não parseável como URI absoluta só quebraria dentro do
            // GrpcChannel, na primeira requisição. Checar aqui move a falha
            // para a inicialização (ValidateOnStart), com a chave nomeada na
            // mensagem.
            .Validate(
                options => Uri.TryCreate(options.IdentityGrpcAddress, UriKind.Absolute, out _),
                "A chave de configuração 'Backends:IdentityGrpcAddress' precisa ser uma URI absoluta (esquema + host + porta).")
            .Validate(
                options => Uri.TryCreate(options.TasksGrpcAddress, UriKind.Absolute, out _),
                "A chave de configuração 'Backends:TasksGrpcAddress' precisa ser uma URI absoluta (esquema + host + porta).")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddScoped<ClientMetadataInterceptor>();

        services.AddScoped<IIdentityBackend, IdentityBackend>();
        services.AddScoped<ITasksBackend, TasksBackend>();

        services.AddGrpcClient<IdentityService.IdentityServiceClient>((provider, options) =>
        {
            var backendOptions = provider.GetRequiredService<IOptions<BackendOptions>>().Value;
            options.Address = new Uri(backendOptions.IdentityGrpcAddress);
        });

        // D-34: ClientMetadataInterceptor acrescenta x-user-id/x-client-date
        // só nas chamadas ao Tasks — o cliente do Identity nunca leva essa
        // metadata (ValidateToken roda antes de existir usuário autenticado).
        services.AddGrpcClient<TasksService.TasksServiceClient>((provider, options) =>
        {
            var backendOptions = provider.GetRequiredService<IOptions<BackendOptions>>().Value;
            options.Address = new Uri(backendOptions.TasksGrpcAddress);
        }).AddInterceptor<ClientMetadataInterceptor>();

        return services;
    }
}
