using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TodoList.Gateway.Api.Configuration;

namespace TodoList.Gateway.Api.Authentication;

/// <summary>
/// Fiação da autenticação do Gateway (BE-40, D-38): <c>AddJwtBearer</c> com a
/// chave pública RSA de <see cref="JwtOptions.PublicKeyPath"/>, substituindo
/// o esquema <c>IdentityToken</c>/<c>IdentityTokenAuthenticationHandler</c>
/// removido por esta task — o Gateway passa a validar o token localmente, sem
/// perguntar ao Identity a cada requisição (D-31 continua valendo, agora por
/// outro mecanismo: só o Identity tem a metade da chave que assina). Chamado
/// a partir de <c>Program.cs</c>, mesmo padrão de
/// <c>AddBackendGrpcClients</c>/<c>AddIdentitySecurity</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayJwtAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // CA-16: sem Jwt:PublicKeyPath (ou com arquivo inexistente/ilegível/
        // não-PEM-SPKI), o host falha no start (ValidateOnStart), nunca na
        // primeira requisição.
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Caminho relativo em Jwt:PublicKeyPath é resolvido contra o
        // ContentRootPath do host, nunca contra o diretório corrente do
        // processo (que varia conforme quem chama `dotnet run`/`dotnet test`)
        // — mesmo motivo e mesmo mecanismo do Identity. PostConfigure roda
        // depois do Bind acima e antes de qualquer IValidateOptions
        // (inclusive JwtOptions.Validate).
        services.PostConfigure<JwtOptions>(options =>
        {
            if (!string.IsNullOrEmpty(options.PublicKeyPath) && !Path.IsPathRooted(options.PublicKeyPath))
            {
                options.PublicKeyPath = Path.Combine(environment.ContentRootPath, options.PublicKeyPath);
            }
        });

        services.AddSingleton<JwtPublicKeyProvider>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Configure<TDep> (em vez do delegate direto de AddJwtBearer) porque
        // a configuração depende de serviços resolvidos por DI
        // (JwtPublicKeyProvider, IOptions<JwtOptions>) — o overload de
        // AddJwtBearer só aceita Action<JwtBearerOptions> sem acesso ao
        // container.
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwtPublicKeyProvider, IOptions<JwtOptions>>((bearerOptions, keyProvider, jwtOptionsAccessor) =>
            {
                var jwtOptions = jwtOptionsAccessor.Value;

                // D-34: sem isto, o handler renomeia "sub" para o URI longo de
                // claim do .NET (ClaimTypes.NameIdentifier) e o
                // ClientMetadataInterceptor (que lê "sub" via
                // JwtClaimTypes.Subject) para de encontrar o claim em
                // silêncio — x-user-id sumiria de toda chamada ao Tasks.
                bearerOptions.MapInboundClaims = false;

                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = keyProvider.PublicKey,

                    // CA-08/CA-09: recusa qualquer outro algoritmo, inclusive
                    // "none" e HS256, mesmo com assinatura sintaticamente
                    // válida para o outro algoritmo.
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],

                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ValidateLifetime = true,

                    // CA-11: um access token de 15 minutos não pode ganhar 5
                    // de tolerância (ClockSkew padrão do framework) — mesma
                    // razão de BE-08.
                    ClockSkew = TimeSpan.Zero,

                    NameClaimType = JwtClaimTypes.Subject,
                };

                bearerOptions.Events = new JwtBearerEvents
                {
                    // CA-15: um único corpo 401 para toda causa (ausente,
                    // expirado, assinatura inválida, algoritmo fora da lista,
                    // issuer/audience errados) — HandleResponse() descarta o
                    // WWW-Authenticate parametrizado (error/error_description)
                    // que o middleware escreveria por padrão e que variaria
                    // conforme a causa.
                    OnChallenge = context =>
                    {
                        context.HandleResponse();

                        var problemDetailsService = context.HttpContext.RequestServices
                            .GetRequiredService<IProblemDetailsService>();

                        return UnauthorizedProblemDetailsWriter.WriteAsync(context.HttpContext, problemDetailsService);
                    },
                };
            });

        return services;
    }
}
