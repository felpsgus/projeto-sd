namespace TodoList.Gateway.Api.Contracts;

/// <summary>
/// Corpo JSON de <c>PUT /api/tasks/{id}</c> (BE-19) — mesma forma de
/// <see cref="CreateTaskHttpRequest"/>, de propósito: são os mesmos quatro
/// campos editáveis (RN-TASK-11), e o contrato REST documenta a semântica de
/// <b>substituição</b> do <c>PUT</c> — um campo ausente no corpo (<c>null</c>)
/// vira o padrão de substituição no Tasks (descrição/vencimento limpos,
/// prioridade volta a Média), nunca "mantém o valor atual" (BE-19, nota
/// técnica). <see cref="Priority"/> e <see cref="DueDate"/> trafegam como
/// <c>string</c> pelo mesmo motivo de <see cref="CreateTaskHttpRequest"/>: um
/// valor fora do domínio esperado precisa virar erro por campo do
/// <see cref="Validation.UpdateTaskHttpRequestValidator"/> (400 com detalhe),
/// não uma falha de binding do Minimal API.
/// </summary>
public sealed record UpdateTaskHttpRequest(string? Title, string? Description, string? Priority, string? DueDate);
