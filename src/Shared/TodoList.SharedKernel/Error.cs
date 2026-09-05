namespace TodoList.SharedKernel;

/// <summary>
/// Erro de negócio: código estável (contrato com o frontend, nunca muda por
/// refactor — só por versionamento), mensagem legível para o cliente e o
/// <see cref="ErrorType"/> que decide o status HTTP na borda de cada serviço.
/// </summary>
/// <param name="Code">Código estável, ex.: <c>auth.invalid_credentials</c>.</param>
/// <param name="Message">Mensagem legível, sempre vinda do catálogo do serviço dono — nunca interpolada com dado interno.</param>
/// <param name="Type">Categoria do erro, usada para o mapeamento HTTP.</param>
public readonly record struct Error(string Code, string Message, ErrorType Type);
