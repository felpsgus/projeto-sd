using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TodoList.Identity.Application.Security;

namespace TodoList.Identity.Infrastructure.Security;

/// <summary>
/// Fiação de segurança do Identity Service: hashing de senha (BE-06) e
/// emissão/validação de access token JWT (BE-08). Registra
/// <see cref="PasswordHashingOptions"/> e <see cref="JwtOptions"/> validados
/// no start e os serviços correspondentes como singleton —
/// <see cref="Pbkdf2PasswordHasher"/>, <see cref="JwtTokenService"/>,
/// <see cref="JwtValidationParameters"/> e <see cref="JwtAccessTokenValidator"/>
/// são todos thread-safe e sem estado mutável, então uma instância serve o
/// processo inteiro. Chamado a partir de <c>Program.cs</c>, mesmo padrão de
/// <c>AddIdentityPersistence</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIdentitySecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<PasswordHashingOptions>()
            .Bind(configuration.GetSection(PasswordHashingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        // BE-08: Jwt:SigningKey passa a ser obrigatória a partir daqui — sem
        // ela (ou com issuer/audience ausentes, ou chave < 32 bytes), o host
        // falha no start (ValidateOnStart), nunca na primeira requisição
        // (CA-02/CA-03). Nunca versionada — vem de user-secrets/variável de
        // ambiente (ver README).
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<JwtValidationParameters>();
        services.AddSingleton<IAccessTokenValidator, JwtAccessTokenValidator>();

        return services;
    }
}
