namespace TodoList.Identity.Application.Users;

/// <summary>Sucesso de <see cref="GetProfileHandler"/>/<see cref="UpdateProfileHandler"/> (BE-14).</summary>
public sealed record ProfileResult(Guid Id, string Email, string DisplayName, DateTime CreatedAt);
