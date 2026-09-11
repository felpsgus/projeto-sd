using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Json;
using Scalar.AspNetCore;
using TodoList.Gateway.Api.Authentication;
using TodoList.Gateway.Api.Backends;
using TodoList.Gateway.Api.Endpoints;
using TodoList.Gateway.Api.ErrorHandling;

var builder = WebApplication.CreateBuilder(args);

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

// ── Backends (BE-36, D-32) ──────────────────────────────────────────────
// Únicos clientes gRPC do Gateway — Identity e Tasks, endereços validados na
// inicialização (CA-22/CA-23). ClientMetadataInterceptor (D-34) é registrado
// só no cliente do Tasks, dentro deste método.
builder.Services.AddBackendGrpcClients(builder.Configuration);

// ── Autenticação (BE-36, D-31) ───────────────────────────────────────────
// IdentityTokenAuthenticationHandler chama ValidateToken via gRPC — a chave
// de assinatura do JWT nunca sai do Identity (D-31). Fallback policy exige
// usuário autenticado por padrão; AllowAnonymous é opt-out explícito
// (/health, POST /api/auth/login, OpenAPI/Scalar).
builder.Services
    .AddAuthentication(IdentityAuthenticationDefaults.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, IdentityTokenAuthenticationHandler>(
        IdentityAuthenticationDefaults.SchemeName, options => { });

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// ── OpenAPI/Scalar (Development, CA-14) ──────────────────────────────────
builder.Services.AddOpenApi();

var app = builder.Build();

// ── Pipeline (BE-36) ─────────────────────────────────────────────────────
// Ordem fixa: erros envolvem tudo → autenticação (quem é você) → autorização
// (pode acessar?) → endpoints, com ValidationFilter<T> aplicado no próprio
// endpoint, antes de qualquer chamada gRPC. Requisição sem token e com
// payload inválido devolve 401, nunca 400 — autenticação vence validação
// (CA-09/CA-11).
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
