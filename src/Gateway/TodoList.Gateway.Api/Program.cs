using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Json;
using Scalar.AspNetCore;
using TodoList.Gateway.Api.Authentication;
using TodoList.Gateway.Api.Backends;
using TodoList.Gateway.Api.Configuration;
using TodoList.Gateway.Api.Endpoints;
using TodoList.Gateway.Api.ErrorHandling;
using TodoList.Gateway.Api.Http;
using TodoList.SharedKernel.Web;

var builder = WebApplication.CreateBuilder(args);

// Log estruturado JSON (BE-24): service=gateway, traceId/spanId, níveis em Serilog:MinimumLevel.
builder.AddStructuredLogging("gateway");

// ── ForwardedHeaders (BE-42) ─────────────────────────────────────────────
// Na VM, o Gateway escuta só em 127.0.0.1:8080 e recebe tráfego exclusivamente
// do nginx (mesma máquina, proxy reverso de /api). KnownProxies vem de
// configuração (ForwardedHeaders:KnownProxies), com o loopback como padrão —
// nunca aberto: sem restringir a proxies conhecidos, qualquer chamador que
// alcançasse a porta diretamente poderia forjar X-Forwarded-For e se passar
// por outro IP de origem.
builder.Services.AddGatewayForwardedHeaders(builder.Configuration);

// ── Erros (BE-36) ───────────────────────────────────────────────────────
// GlobalExceptionHandler + ProblemDetails com traceId em todo corpo de erro
// (400/401/403/404/409/500/503) — registrado primeiro porque o middleware do
// pipeline (mais abaixo) precisa envolver tudo o que vem depois dele.
builder.Services.AddApiErrorHandling();

// BadHttpRequestException (corpo JSON malformado, CA-07) só chega ao
// GlobalExceptionHandler se o binder de Minimal API a lançar em vez de
// engolir silenciosamente — sem isto, um JSON quebrado vira 400 "vazio" sem
// ProblemDetails.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

// Enums trafegariam como string se algum DTO os expusesse diretamente (mesmo
// padrão de BE-17) — hoje nenhum expõe (priority/status já são string em
// Contracts/), mas a configuração fica pronta para o dia em que um expuser.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// ── Paginação (BE-41, D-09/D-33) ─────────────────────────────────────────
// Cópia própria do Gateway dos mesmos limites do Tasks (Paging:DefaultPageSize/
// MaxPageSize) — o Gateway não referencia o projeto do Tasks (D-33), então
// esta duplicação é deliberada; a do Tasks continua sendo a autoridade final
// (defesa em profundidade, CA-19).
builder.Services
    .AddOptions<PagingOptions>()
    .Bind(builder.Configuration.GetSection(PagingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ── Backends (BE-36, D-32) ──────────────────────────────────────────────
// Únicos clientes gRPC do Gateway — Identity e Tasks, endereços validados na
// inicialização (CA-22/CA-23). ClientMetadataInterceptor (D-34) é registrado
// só no cliente do Tasks, dentro deste método.
builder.Services.AddBackendGrpcClients(builder.Configuration);

// ── Cookie do refresh token (BE-09/BE-10, D-20) ──────────────────────────
// RefreshCookie:Secure (padrão true) — false só em deploy HTTP puro por IP.
builder.Services.AddRefreshCookie(builder.Configuration);

// ── Autenticação (BE-40, D-38) ───────────────────────────────────────────
// AddJwtBearer valida o token localmente, com a chave pública RSA carregada
// de Jwt:PublicKeyPath — a chave de assinatura (privada) nunca sai do
// Identity (D-31/D-38); o Gateway só recebe a metade que verifica. O Gateway
// não chama o Identity para autenticar requisições. Fallback
// policy exige usuário autenticado por padrão; AllowAnonymous é opt-out
// explícito (/health, POST /api/auth/login, OpenAPI/Scalar).
builder.Services.AddGatewayJwtAuthentication(builder.Configuration, builder.Environment);

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// ── OpenAPI/Scalar (Development, CA-14) ──────────────────────────────────
builder.Services.AddOpenApi();

var app = builder.Build();

// ── Pipeline (BE-36/BE-42) ────────────────────────────────────────────────
// UseForwardedHeaders é a primeira coisa no pipeline — antes de erros,
// autenticação, autorização e endpoints, e antes de qualquer log que use o IP
// de origem — para que HttpContext.Connection.RemoteIpAddress já reflita o
// cliente real (não o nginx) no restante do pipeline.
app.UseForwardedHeaders();

// Ordem fixa: erros envolvem tudo → autenticação (quem é você) → autorização
// (pode acessar?) → endpoints, com ValidationFilter<T> aplicado no próprio
// endpoint, antes de qualquer chamada gRPC. Requisição sem token e com
// payload inválido devolve 401, nunca 400 — autenticação vence validação
// (CA-09/CA-11).
// Log de requisição logo abaixo do ForwardedHeaders e acima do tratamento de erros: vê o status
// final (inclusive o 500 do handler global). userId só do claim sub validado — nunca de header do cliente.
app.UseStructuredRequestLogging(http => http.User.FindFirst(JwtClaimTypes.Subject)?.Value);

app.UseApiErrorHandling();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.MapEndpoints();

app.Run();

// Exposto para TodoList.Gateway.UnitTests/TodoList.Gateway.IntegrationTests via WebApplicationFactory<Program>.
public partial class Program
{
}
