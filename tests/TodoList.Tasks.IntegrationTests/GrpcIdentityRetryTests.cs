extern alias IdentityApi;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TodoList.Tasks.Application.Identity;
using TodoList.Tasks.Infrastructure.Identity;
using Xunit;
using FakeIdentityBase = IdentityApi::TodoList.Contracts.Identity.V1.IdentityService.IdentityServiceBase;
using FakeValidateUserRequest = IdentityApi::TodoList.Contracts.Identity.V1.ValidateUserRequest;
using FakeValidateUserResponse = IdentityApi::TodoList.Contracts.Identity.V1.ValidateUserResponse;

namespace TodoList.Tasks.IntegrationTests;

/// <summary>BE-43 (D-44), CA-08/CA-09 — retry nativo do Grpc.Net.Client em <c>ValidateUser</c>, só em <c>Unavailable</c>.</summary>
public class GrpcIdentityRetryTests
{
    [Fact] // CA-08
    public async Task ValidateUserAsync_UnavailableUmaVez_RetentaERetornaExiste()
    {
        var fake = new FakeIdentity(failuresBeforeSuccess: 1);
        using var identity = await StartFakeIdentityAsync(fake);
        using var host = BuildTasksHost(identity);
        var gateway = host.Services.GetRequiredService<IIdentityGateway>();

        var result = await gateway.ValidateUserAsync(Guid.NewGuid(), CancellationToken.None);

        result.Exists.Should().BeTrue();
        fake.Calls.Should().Be(2);
    }

    [Fact] // CA-09 — D-28 (fail-closed) inalterado
    public async Task ValidateUserAsync_SempreUnavailable_ParaEmTres()
    {
        var fake = new FakeIdentity(failuresBeforeSuccess: int.MaxValue);
        using var identity = await StartFakeIdentityAsync(fake);
        using var host = BuildTasksHost(identity);
        var gateway = host.Services.GetRequiredService<IIdentityGateway>();

        var act = async () => await gateway.ValidateUserAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<IdentityUnavailableException>();
        fake.Calls.Should().Be(3);
    }

    private static async Task<IHost> StartFakeIdentityAsync(FakeIdentity fake)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddGrpc();
                    services.AddSingleton(fake);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGrpcService<FakeIdentity>());
                }))
            .Build();
        await host.StartAsync();

        return host;
    }

    private static IHost BuildTasksHost(IHost identity)
    {
        var server = identity.GetTestServer();
        var settings = new Dictionary<string, string?>
        {
            ["Identity:GrpcAddress"] = server.BaseAddress.ToString(),
            ["Identity:GrpcTimeoutSeconds"] = "5",
        };

        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.Sources.Clear();
                configuration.AddInMemoryCollection(settings);
            })
            .ConfigureServices((context, services) =>
                services.AddIdentityGrpcClient(context.Configuration)
                    .ConfigurePrimaryHttpMessageHandler(() => server.CreateHandler()))
            .Build();
    }

    private sealed class FakeIdentity : FakeIdentityBase
    {
        private readonly int _failuresBeforeSuccess;
        private int _calls;

        public FakeIdentity(int failuresBeforeSuccess) => _failuresBeforeSuccess = failuresBeforeSuccess;

        public int Calls => _calls;

        public override Task<FakeValidateUserResponse> ValidateUser(FakeValidateUserRequest request, ServerCallContext context)
        {
            if (Interlocked.Increment(ref _calls) <= _failuresBeforeSuccess)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, string.Empty));
            }

            return Task.FromResult(new FakeValidateUserResponse { Exists = true, DisplayName = "Fulano" });
        }
    }
}
