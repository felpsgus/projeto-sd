using Grpc.Core;
using Grpc.Net.Client.Configuration;

namespace TodoList.Gateway.Api.Backends;

/// <summary>
/// Retry nativo do Grpc.Net.Client (BE-43, D-44): só as RPCs de leitura
/// informadas e só em <c>Unavailable</c> — escrita nunca é retentada, para não
/// duplicar efeito colateral.
/// </summary>
internal static class GrpcRetry
{
    public static ServiceConfig ForReadOnly(int maxAttempts, params MethodName[] methods)
    {
        var methodConfig = new MethodConfig
        {
            RetryPolicy = new RetryPolicy
            {
                MaxAttempts = maxAttempts,
                InitialBackoff = TimeSpan.FromMilliseconds(200),
                MaxBackoff = TimeSpan.FromSeconds(1),
                BackoffMultiplier = 2,
                RetryableStatusCodes = { StatusCode.Unavailable },
            },
        };

        foreach (var method in methods)
        {
            methodConfig.Names.Add(method);
        }

        return new ServiceConfig { MethodConfigs = { methodConfig } };
    }
}
