using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TodoList.Contracts.Identity.V1;
using TodoList.Tasks.Application.Identity;

namespace TodoList.Tasks.Infrastructure.Identity;

/// <summary>
/// Registro do cliente gRPC tipado do Identity e da abstração
/// <see cref="IIdentityGateway"/> (BE-27). Usa <c>AddGrpcClient</c> — nunca
/// <c>GrpcChannel.ForAddress</c> dentro de um método de chamada (CA-11): o
/// canal é caro de criar e a fábrica cuida do seu ciclo de vida, do mesmo
/// jeito que <c>IHttpClientFactory</c> (nota técnica de BE-27).
/// </summary>
public static class ServiceCollectionExtensions
{
    // h2c sem TLS funciona sem nenhum ajuste do lado cliente: o Identity
    // declara o endpoint Kestrel como Protocols=Http2 (BE-26/BE-30), que é a
    // solução que a especificação manda adotar. O switch
    // System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport era exigido
    // no .NET Core 3.x e é desnecessário aqui — verificado com chamada real
    // contra o Identity em execução.
    public static IHttpClientBuilder AddIdentityGrpcClient(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<IdentityGrpcOptions>()
            .Bind(configuration.GetSection(IdentityGrpcOptions.SectionName))
            .ValidateDataAnnotations()
            // CA-02 (BE-30): [Required] só garante que a string não está vazia
            // — "identity-service" (sem esquema) passaria por ela e só
            // quebraria dentro do GrpcChannel, na primeira chamada, com um
            // erro que não aponta para configuração nenhuma. Uri.TryCreate
            // com UriKind.Absolute é a mesma checagem que GrpcChannel faria,
            // só que na inicialização — e com a chave nomeada na mensagem.
            .Validate(
                options => Uri.TryCreate(options.GrpcAddress, UriKind.Absolute, out _),
                "A chave de configuração 'Identity:GrpcAddress' precisa ser uma URI absoluta (esquema + host + "
                + "porta) — o valor atual não é parseável como uma.")
            .ValidateOnStart();

        services.AddScoped<IIdentityGateway, GrpcIdentityGateway>();

        return services.AddGrpcClient<IdentityService.IdentityServiceClient>((provider, options) =>
        {
            var grpcOptions = provider.GetRequiredService<IOptions<IdentityGrpcOptions>>().Value;
            options.Address = new Uri(grpcOptions.GrpcAddress);
        });
    }
}
