using FluentValidation;
using TodoList.Tasks.Api.Configuration;
using TodoList.Tasks.Api.Endpoints;
using TodoList.Tasks.Api.ErrorHandling;
using TodoList.Tasks.Api.Grpc;
using TodoList.Tasks.Api.Security;
using TodoList.Tasks.Application.Security;
using TodoList.Tasks.Application.Tasks;
using TodoList.Tasks.Infrastructure.Identity;
using TodoList.Tasks.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

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

// BE-17 (D-08): limite de tarefas ativas, lido pelo CreateTaskHandler.
builder.Services
    .AddOptions<TaskOptions>()
    .Bind(builder.Configuration.GetSection(TaskOptions.SectionName))
    .ValidateOnStart();

// BE-35 (D-30 fechada, D-34): ICurrentUser/IClientDate são abstrações de
// Application — a escolha de implementação é decidida uma vez, aqui, na
// borda. CallerIdentityCurrentUser é a ÚNICA implementação registrada (CA-10)
// — sem factory condicional: o Tasks confia no chamador (metadata gRPC
// x-user-id) de forma permanente, não mais sob uma flag de ambiente.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IClientDate, HttpContextClientDate>();
builder.Services.AddScoped<ICurrentUser, CallerIdentityCurrentUser>();

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

// BE-35 (D-37): gRPC Health Checking Protocol. AddGrpcHealthChecks() reusa o
// MESMO IHealthChecksBuilder de AddHealthChecks() acima (chama-o
// internamente, TryAdd-style) e mapeia o serviço "" (sem nome — o que
// grpc.health.v1.Health/Check consulta sem especificar HealthCheckRequest.Service)
// para TODOS os checks registrados, que hoje é só AddTasksDatabaseHealthCheck
// (tag "ready"). Decisão (D-37): é o mesmo check de /health/ready — o
// liveness puro (processo de pé) já é garantido pelo próprio Kestrel aceitar
// a conexão TCP/HTTP2, então o que vale reportar por este canal para o probe
// do Cloud Run é a readiness real (conectividade com o banco), não um "always
// healthy" que esconderia o serviço realmente fora do ar.
builder.Services.AddGrpcHealthChecks();

builder.Services.AddGrpc(options =>
{
    // BE-35: exceção não tratada dentro de um RPC NÃO DEVE vazar stack
    // trace/mensagem interna ao chamador fora de Development — mesma regra
    // de GlobalExceptionHandler (BE-03, CA-05) para o transporte HTTP.
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
})
    // BE-35: RequireCallerIdentityInterceptor é registrado SÓ para
    // TasksGrpcService — nunca globalmente (AddGrpc(options => ...)), porque
    // isso bloquearia grpc.health.v1.Health/Check (D-37), que não manda
    // x-user-id e não deveria precisar.
    .AddServiceOptions<TasksGrpcService>(options =>
        options.Interceptors.Add<RequireCallerIdentityInterceptor>());

var app = builder.Build();

app.UseApiErrorHandling();

app.MapEndpoints();
app.MapGrpcService<TasksGrpcService>();
app.MapGrpcHealthChecksService();

app.Run();

// Exposto para TodoList.Tasks.IntegrationTests via WebApplicationFactory<Program>.
public partial class Program
{
}
