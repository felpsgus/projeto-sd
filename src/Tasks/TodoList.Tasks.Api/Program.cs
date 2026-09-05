using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using TodoList.Tasks.Api.Configuration;
using TodoList.Tasks.Api.Endpoints;
using TodoList.Tasks.Api.ErrorHandling;
using TodoList.Tasks.Api.Security;
using TodoList.Tasks.Api.Startup;
using TodoList.Tasks.Application.Security;
using TodoList.Tasks.Application.Tasks;
using TodoList.Tasks.Infrastructure.Identity;
using TodoList.Tasks.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddApiErrorHandling();
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// BE-17, nota técnica: enums trafegam como string ("High"), nunca como
// número — contrato legível e resistente a reordenação do enum.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Abstração de tempo (BE-02, CA-08): nada de DateTime.UtcNow espalhado pelo
// código. Testes substituem por um FakeTimeProvider.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services
    .AddOptions<ServiceOptions>()
    .Bind(builder.Configuration.GetSection(ServiceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// BE-17 (D-08): limite de tarefas ativas, lido pelo CreateTaskHandler.
builder.Services
    .AddOptions<TaskOptions>()
    .Bind(builder.Configuration.GetSection(TaskOptions.SectionName))
    .ValidateOnStart();

// BE-29 (D-30): só a fiação HTTP lê esta flag — a Application não a conhece.
builder.Services
    .AddOptions<TasksCreationOptions>()
    .Bind(builder.Configuration.GetSection(TasksCreationOptions.SectionName))
    .ValidateOnStart();

// BE-13/BE-29: ICurrentUser/IClientDate são abstrações de Application — a
// escolha de implementação é decidida uma vez, aqui, na borda.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IClientDate, HttpContextClientDate>();

// A implementação é escolhida por um factory resolvido por requisição — não
// por um "if" fora do container decidido uma vez no boot — para que a
// decisão sempre reflita o IOptions<TasksCreationOptions> efetivamente
// vinculado (o mesmo valor que TaskEndpoints consulta para a allowlist de
// AllowAnonymous, e que StartupLog usa para o aviso de inicialização),
// mesmo quando a configuração é montada em etapas (caso de
// WebApplicationFactory nos testes de integração, que injeta overrides
// depois deste ponto do arquivo, mas antes do primeiro request).
builder.Services.AddScoped<ICurrentUser>(serviceProvider =>
{
    var creationOptions = serviceProvider.GetRequiredService<IOptions<TasksCreationOptions>>().Value;

    // TODO(dono: time Backend — BE-13; prazo: antes de qualquer ambiente
    // exposto fora de máquina local/demo): trocar por uma implementação de
    // ICurrentUser baseada em claim (BE-13) e remover o ramo
    // AllowAnonymousCreate — ver TodoList.Tasks.Api.Security.HeaderCurrentUser
    // e BE-29 (D-30).
    return creationOptions.AllowAnonymousCreate
        ? new HeaderCurrentUser(serviceProvider.GetRequiredService<IHttpContextAccessor>())
        : new NotYetAuthenticatedCurrentUser();
});

builder.Services.AddScoped<CreateTaskHandler>();

// Cliente gRPC do Identity (BE-27), consumido a partir desta etapa (BE-28)
// por CreateTaskHandler via IIdentityGateway.
builder.Services.AddIdentityGrpcClient(builder.Configuration);

// Persistência (BE-02): DbContext + interceptor de auditoria/soft delete +
// IUnitOfWork. Connection string vem de configuração — dotnet user-secrets em
// dev, variável de ambiente no CI — nunca versionada (CA-11). Nunca
// EnsureCreated()/Migrate() automático aqui: migrations são aplicadas por
// comando explícito (ver README).
builder.Services.AddTasksPersistence(builder.Configuration);

// CA-03/CA-04: liveness (/health) não depende de nada; readiness
// (/health/ready) roda só os checks marcados "ready" — hoje, o banco.
builder.Services.AddHealthChecks()
    .AddTasksDatabaseHealthCheck();

var app = builder.Build();

app.UseApiErrorHandling();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// BE-29, CA-06: aviso de inicialização quando o modo provisório está ligado.
if (app.Services.GetRequiredService<IOptions<TasksCreationOptions>>().Value.AllowAnonymousCreate)
{
    StartupLog.AllowAnonymousCreateEnabled(app.Logger);
}

app.MapEndpoints();

app.Run();

// Exposto para TodoList.Tasks.IntegrationTests via WebApplicationFactory<Program>.
public partial class Program
{
}
