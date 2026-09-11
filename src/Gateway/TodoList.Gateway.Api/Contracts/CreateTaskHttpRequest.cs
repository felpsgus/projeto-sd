namespace TodoList.Gateway.Api.Contracts;

/// <summary>
/// Corpo JSON de <c>POST /api/tasks</c> (BE-36, CA-05/CA-06/CA-08).
/// <see cref="Priority"/> e <see cref="DueDate"/> trafegam como <c>string</c>
/// — não como o enum/tipo forte que o proto usa — de propósito: um valor
/// fora do domínio esperado (ex.: <c>"priority": "Urgente"</c>) precisa virar
/// erro por campo do <see cref="Validation.CreateTaskHttpRequestValidator"/>
/// (400 com detalhe), não uma falha de binding do Minimal API — o binder não
/// sabe produzir um <c>ValidationProblemDetails</c> por campo para um enum
/// desconhecido, só uma exceção genérica.
/// </summary>
public sealed record CreateTaskHttpRequest(string? Title, string? Description, string? Priority, string? DueDate);
