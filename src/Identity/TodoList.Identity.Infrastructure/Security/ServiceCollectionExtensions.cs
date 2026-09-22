using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TodoList.Identity.Application.Security;

namespace TodoList.Identity.Infrastructure.Security;

/// <summary>
/// Fiação de segurança do Identity Service: hashing de senha (BE-06) e
/// emissão/validação de access token JWT RS256 (BE-08, D-38). Registra
/// <see cref="PasswordHashingOptions"/> e <see cref="JwtOptions"/> validados
/// no start e os serviços correspondentes como singleton —
/// <see cref="Pbkdf2PasswordHasher"/>, <see cref="RsaSigningKeyProvider"/>,
/// <see cref="JwtTokenService"/>, <see cref="JwtValidationParameters"/> e
/// <see cref="JwtAccessTokenValidator"/> são todos thread-safe e sem estado
/// mutável após a construção, então uma instância serve o processo inteiro.
/// Chamado a partir de <c>Program.cs</c>, mesmo padrão de
/// <c>AddIdentityPersistence</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIdentitySecurity(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services
            .AddOptions<PasswordHashingOptions>()
            .Bind(configuration.GetSection(PasswordHashingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        // BE-40/D-38: Jwt:PrivateKeyPath passa a ser obrigatória a partir
        // daqui — sem ela (ou apontando para arquivo inexistente/ilegível/
        // não-PEM-PKCS8/chave < 2048 bits), o host falha no start
        // (ValidateOnStart), nunca na primeira requisição (CA-03 a CA-06).
        // Nunca versionada — vem de variável de ambiente/secret (VM,
        // compose) ou de appsettings.Development.json apontando para
        // .secrets/jwt/ (ignorado pelo git) em desenvolvimento local.
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Caminho relativo em Jwt:PrivateKeyPath é resolvido contra o
        // ContentRootPath do host, nunca contra o diretório corrente do
        // processo (que varia conforme quem chama `dotnet run`/`dotnet test`).
        // PostConfigure é o ponto certo: roda depois do Bind acima e antes de
        // qualquer IValidateOptions (inclusive JwtOptions.Validate), e é
        // aqui — na camada Infrastructure, chamada a partir de Program.cs,
        // que por sua vez recebe IHostEnvironment do builder — que a
        // informação de ContentRootPath está disponível sem a Infrastructure
        // precisar depender de IWebHostEnvironment diretamente.
        services.PostConfigure<JwtOptions>(options =>
        {
            if (!string.IsNullOrEmpty(options.PrivateKeyPath) && !Path.IsPathRooted(options.PrivateKeyPath))
            {
                options.PrivateKeyPath = Path.Combine(environment.ContentRootPath, options.PrivateKeyPath);
            }
        });

        services.AddSingleton<RsaSigningKeyProvider>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<JwtValidationParameters>();
        services.AddSingleton<IAccessTokenValidator, JwtAccessTokenValidator>();

        return services;
    }
}
