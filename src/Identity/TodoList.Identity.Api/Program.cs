using FluentValidation;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using TodoList.Identity.Api.Configuration;
using TodoList.Identity.Api.Endpoints;
using TodoList.Identity.Api.ErrorHandling;
using TodoList.Identity.Api.Grpc;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Infrastructure.Persistence;
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

// Seed de usuários de demonstração (BE-04/BE-26, CA-14): desligado por
// padrão, ligado só por UserStore:SeedDemoUsers=true — nunca
// EnsureCreated()/Migrate() automático aqui, a tabela precisa já existir
// (ver README, seção "Migrations").
if (app.Services.GetRequiredService<IOptions<UserStoreOptions>>().Value.SeedDemoUsers)
{
    using var seedScope = app.Services.CreateScope();
    await seedScope.ServiceProvider.GetRequiredService<DemoUserSeeder>().SeedAsync(CancellationToken.None);
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
