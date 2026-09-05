using System.ComponentModel.DataAnnotations;

namespace TodoList.Identity.Api.Configuration;

/// <summary>
/// Configuração tipada mínima do serviço, vinculada com <c>IOptions&lt;T&gt;</c>
/// e validada na inicialização (<c>ValidateOnStart</c>) — ver CA-08 de BE-01.
/// Ausência de <see cref="DisplayName"/> derruba a inicialização, em vez de
/// falhar silenciosamente na primeira requisição.
/// </summary>
public sealed class ServiceOptions
{
    public const string SectionName = "Service";

    [Required(AllowEmptyStrings = false)]
    public string DisplayName { get; init; } = string.Empty;
}
