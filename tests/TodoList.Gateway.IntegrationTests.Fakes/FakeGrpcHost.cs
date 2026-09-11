using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TodoList.Gateway.IntegrationTests.Fakes;

/// <summary>
/// Servidor gRPC falso, in-process (BE-36 — abordagem obrigatória de teste):
/// hospeda uma única instância de <typeparamref name="TService"/> (herdando
/// de um <c>*ServiceBase</c> gerado pelo proto) num <see cref="TestServer"/>
/// real (<c>UseTestServer()</c> + <c>AddGrpc()</c> + <c>MapGrpcService&lt;TService&gt;()</c>),
/// para que o cliente gRPC do Gateway exercite de verdade o interceptor, os
/// deadlines e os trailers — nunca um mock de interface no lugar do backend.
/// </summary>
public sealed class FakeGrpcHost<TService> : IDisposable
    where TService : class
{
    private readonly IHost _host;

    public FakeGrpcHost(TService serviceInstance)
    {
        _host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddGrpc();
                    services.AddSingleton(serviceInstance);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGrpcService<TService>());
                });
            })
            .Start();
    }

    /// <summary>Handler HTTP em memória — usado em <c>ConfigurePrimaryHttpMessageHandler</c> do cliente gRPC do Gateway.</summary>
    public HttpMessageHandler CreateHandler() => _host.GetTestServer().CreateHandler();

    public void Dispose() => _host.Dispose();
}
