using FluentValidation;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TodoList.SharedKernel.Web;
using TodoList.Tasks.Api.ErrorHandling;
using TodoList.Tasks.Api.Grpc;
using TodoList.Tasks.Api.Security;
using TodoList.Tasks.Application.Security;
using TodoList.Tasks.Application.Tasks;
using TodoList.Tasks.Infrastructure.Identity;
using TodoList.Tasks.Infrastructure.Persistence;
using TodoList.Tasks.Infrastructure.Retention;

var builder = WebApplication.CreateBuilder(args);

// Log estruturado JSON (BE-24): service=tasks, traceId/spanId, níveis em Serilog:MinimumLevel.
builder.AddStructuredLogging("tasks");

// Handler global de exceções + ProblemDetails com traceId (BE-03, CA-03/CA-05/CA-06).
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.GetTraceId();
    };
});
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
    .ValidateDataAnnotations()
    .ValidateOnStart();

// BE-41 (D-09): tamanho de página, lido pelo ListTasksHandler. Criada aqui
// porque BE-22 (que vai reaproveitar o mesmo tipo) ainda não foi implementada
// (nota técnica de BE-41).
builder.Services
    .AddOptions<PagingOptions>()
    .Bind(builder.Configuration.GetSection(PagingOptions.SectionName))
    .ValidateDataAnnotations()
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

// BE-41 (recorte de BE-22/BE-18): listagem e consulta por id.
builder.Services.AddScoped<ListTasksHandler>();
builder.Services.AddScoped<GetTaskHandler>();

// Fase 1 do PLANO-REGRAS-RESTANTES: editar (BE-19), concluir/reabrir (BE-20)
// e remover (BE-21) uma tarefa própria.
builder.Services.AddScoped<UpdateTaskHandler>();
builder.Services.AddScoped<CompleteTaskHandler>();
builder.Services.AddScoped<ReopenTaskHandler>();
builder.Services.AddScoped<DeleteTaskHandler>();

// Cliente gRPC do Identity (BE-27), consumido a partir desta etapa (BE-28)
// por CreateTaskHandler via IIdentityGateway.
builder.Services.AddIdentityGrpcClient(builder.Configuration);

// Persistência (BE-02): DbContext + interceptor de auditoria/soft delete +
// IUnitOfWork. Connection string vem de configuração — dotnet user-secrets em
// dev, variável de ambiente no CI — nunca versionada (CA-11). Nunca
// EnsureCreated()/Migrate() automático aqui: migrations são aplicadas por
// comando explícito (ver README).
builder.Services.AddTasksPersistence(builder.Configuration);

// BE-23: expurgo de tarefas removidas (D-12/D-13).
builder.Services.AddDataRetention<TasksRetentionPurger>(builder.Configuration);

// CA-03/CA-04: liveness (/health) não depende de nada; readiness
// (/health/ready) roda só os checks marcados "ready" — hoje, o banco.
builder.Services.AddHealthChecks()
    .AddTasksDatabaseHealthCheck()
    // BE-24: Identity inalcançável degrada o /health/ready (200 "Degraded"), nunca o derruba.
    .AddCheck<IdentityReachabilityHealthCheck>(
        "identity-grpc", HealthStatus.Degraded, tags: ["ready", IdentityReachabilityHealthCheck.Tag]);

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
// O check do Identity (tag identity-reachability) fica de fora: o probe do Cloud Run avalia só o banco (D-37).
builder.Services.AddGrpcHealthChecks(options =>
    options.Services.Map(string.Empty, check => !check.Tags.Contains(IdentityReachabilityHealthCheck.Tag)));

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

// Log de requisição (inclui as chamadas gRPC recebidas); userId vem da metadata x-user-id, quando houver.
app.UseStructuredRequestLogging(StructuredLogging.CallerUserId);

app.UseExceptionHandler();

app.MapHealthEndpoints();
app.MapGrpcService<TasksGrpcService>();
app.MapGrpcHealthChecksService();

app.Run();

// Exposto para TodoList.Tasks.IntegrationTests via WebApplicationFactory<Program>.
public partial class Program
{
}
