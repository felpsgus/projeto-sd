namespace TodoList.Identity.Application.Authentication;

/// <summary>Sucesso de <see cref="RegisterUserHandler.HandleAsync"/> (BE-07, CA-01).</summary>
public sealed record RegisterUserResult(Guid Id, string Email, string DisplayName, DateTime CreatedAt);
