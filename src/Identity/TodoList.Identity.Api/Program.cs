using FluentValidation;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using TodoList.Identity.Api.Configuration;
using TodoList.Identity.Api.Endpoints;
using TodoList.Identity.Api.ErrorHandling;
using TodoList.Identity.Api.Grpc;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Identity.Infrastructure.Security;
using TodoList.Identity.Infrastructure.Users;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddGrpc();
builder.Services.AddApiErrorHandling();
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// Abstração de tempo (BE-02, CA-08): nada de DateTime.UtcNow espalhado pelo
// código. Testes substituem por um FakeTimeProvider.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services
    .AddOptions<ServiceOptions>()
    .Bind(builder.Configuration.GetSection(ServiceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<UserStoreOptions>()
    .Bind(builder.Configuration.GetSection(UserStoreOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Persistência (BE-02): DbContext + interceptor de auditoria/soft delete +
// IUnitOfWork + IUserRepository (BE-04). Connection string vem de configuração
// — dotnet user-secrets em dev, variável de ambiente no CI — nunca versionada
// (CA-11). Nunca EnsureCreated()/Migrate() automático aqui: migrations são
// aplicadas por comando explícito (ver README).
builder.Services.AddIdentityPersistence(builder.Configuration);

// Hashing de senha (BE-06): PasswordHashingOptions validado no start +
// IPasswordHasher singleton (Pbkdf2PasswordHasher).
builder.Services.AddIdentitySecurity(builder.Configuration);

// Seleção do store de usuários por configuração (BE-26). "InMemory" não tem
// dependência escopada, então pode ser Singleton de verdade (uma instância só
// no processo inteiro — é o que faz o log de aviso do CA-15 aparecer uma
// única vez). "Persisted" (BE-04, CA-13) depende de IUserRepository, que por
// sua vez depende do IdentityDbContext — Scoped. Por isso IUserLookup em si
// também precisa ser Scoped: resolver um serviço Scoped a partir de um
// registro Singleton criaria uma "captive dependency" (o DbContext escopado
// ficaria preso dentro de uma instância que vive para sempre) — o próprio
// ASP.NET Core recusa isso em Development (falha de validação de escopo). A
// fábrica abaixo só decide QUAL registro usar; cada implementação mantém o
// tempo de vida que ela mesma precisa.
builder.Services.AddSingleton<InMemoryUserLookup>();
builder.Services.AddScoped<PersistedUserLookup>();
builder.Services.AddScoped<DemoUserSeeder>();

// Login (BE-33): LoginHandler é Scoped porque depende de IUserRepository
// (Scoped, por sua vez do IdentityDbContext). DummyPasswordHash é Singleton
// de propósito — o hash dummy precisa ser fixo por processo, não recalculado
// a cada requisição (ver XML doc de DummyPasswordHash).
builder.Services.AddSingleton<DummyPasswordHash>();
builder.Services.AddScoped<LoginHandler>();

builder.Services.AddScoped<IUserLookup>(sp =>
{
    var provider = sp.GetRequiredService<IOptions<UserStoreOptions>>().Value.Provider;

    return provider switch
    {
        UserStoreOptions.InMemoryProvider => sp.GetRequiredService<InMemoryUserLookup>(),
        UserStoreOptions.PersistedProvider => sp.GetRequiredService<PersistedUserLookup>(),
        _ => throw new NotSupportedException(
            $"Provedor de store de usuário '{provider}' não implementado (ver BE-04/BE-26)."),
    };
});

// CA-03/CA-04: liveness (/health) não depende de nada; readiness
// (/health/ready) roda só os checks marcados "ready" — hoje, o banco.
builder.Services.AddHealthChecks()
    .AddIdentityDatabaseHealthCheck();

var app = builder.Build();

app.UseApiErrorHandling();

// Força a resolução do IUserLookup na inicialização: com o seed em memória,
// isso garante o log de aviso do CA-15 (BE-26) mesmo antes da primeira chamada
// gRPC. IUserLookup agora é Scoped (comentário acima) — resolver direto de
// app.Services (o provedor raiz) violaria a checagem de "captive dependency"
// do ASP.NET Core em Development, então o warm-up cria seu próprio escopo,
// exatamente como uma requisição real criaria.
using (var warmUpScope = app.Services.CreateScope())
{
    warmUpScope.ServiceProvider.GetRequiredService<IUserLookup>();
}

var userStoreOptions = app.Services.GetRequiredService<IOptions<UserStoreOptions>>().Value;

// BE-33, CA-11: com UserStore:Provider=InMemory não existe senha/hash
// associado ao seed em memória — Login sempre nega (decisão na borda, não na
// Application). O aviso sai uma única vez aqui, na inicialização, nunca a
// cada chamada de Login.
if (userStoreOptions.Provider == UserStoreOptions.InMemoryProvider)
{
    StartupLog.LoginNotSupportedWithInMemoryProvider(app.Services.GetRequiredService<ILogger<Program>>());
}

// Seed de usuários de demonstração (BE-04/BE-26/BE-33, CA-14 de BE-26, CA-08
// de BE-33): desligado por padrão, ligado só por UserStore:SeedDemoUsers=true
// — nunca EnsureCreated()/Migrate() automático aqui, a tabela precisa já
// existir (ver README, seção "Migrations"). DemoUserPassword é obrigatória
// quando SeedDemoUsers=true (UserStoreOptions.Validate, ValidateOnStart), então
// já está garantida não-nula neste ponto.
if (userStoreOptions.SeedDemoUsers)
{
    using var seedScope = app.Services.CreateScope();
    await seedScope.ServiceProvider.GetRequiredService<DemoUserSeeder>().SeedAsync(userStoreOptions.DemoUserPassword!, CancellationToken.None);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapEndpoints();
app.MapGrpcService<IdentityGrpcService>();

await app.RunAsync();

// Exposto para TodoList.Identity.IntegrationTests via WebApplicationFactory<Program>.
public partial class Program
{
}

/// <summary>
/// Log de inicialização do host (BE-33, CA-11) — não é um <c>LoggerMessage</c>
/// de hot path como os de <c>IdentityGrpcService</c>, mas segue o mesmo
/// mecanismo de logging estruturado (source-generated), em vez de string
/// interpolada solta.
/// </summary>
internal static partial class StartupLog
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Identity está com UserStore:Provider=InMemory — Login sempre responde succeeded=false " +
            "(não há senha/hash associado ao seed em memória). Use UserStore:Provider=Persisted para autenticar de verdade.")]
    public static partial void LoginNotSupportedWithInMemoryProvider(ILogger logger);
}
