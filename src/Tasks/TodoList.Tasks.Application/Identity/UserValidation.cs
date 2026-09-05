namespace TodoList.Tasks.Application.Identity;

/// <summary>
/// Resultado, em vocabulário próprio da <c>Application</c>, de perguntar ao
/// Identity sobre um usuário — não é (nem referencia) o
/// <c>ValidateUserResponse</c> gerado a partir do <c>.proto</c> (BE-27, CA-01).
/// </summary>
/// <param name="Exists">O usuário existe no Identity.</param>
/// <param name="Active">O usuário existe e está ativo (RN-USER-04).</param>
/// <param name="DisplayName">Nome de exibição do dono, vazio quando <paramref name="Exists"/> é <c>false</c>.</param>
public sealed record UserValidation(bool Exists, bool Active, string DisplayName);
