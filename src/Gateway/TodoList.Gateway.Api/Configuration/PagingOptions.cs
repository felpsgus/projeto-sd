using System.ComponentModel.DataAnnotations;

namespace TodoList.Gateway.Api.Configuration;

/// <summary>
/// Seção <c>Paging</c> do Gateway (BE-41, D-09) — cópia deliberada da mesma
/// configuração do Tasks (<c>TodoList.Tasks.Application.Tasks.PagingOptions</c>),
/// com os mesmos valores padrão (20/100). O Gateway não pode referenciar o
/// projeto do Tasks para reaproveitar o tipo (D-33: nenhuma referência de
/// projeto ao Tasks), então esta duplicação é intencional — documentada aqui
/// e na nota técnica de BE-41 ("por que os limites de paginação são os
/// mesmos de BE-22, e não um valor novo"). Validada na inicialização
/// (<c>ValidateOnStart</c>, mesmo padrão de <see cref="BackendOptions"/>).
/// </summary>
public sealed class PagingOptions
{
    /// <summary>Nome da seção de configuração (D-09).</summary>
    public const string SectionName = "Paging";

    /// <summary>Tamanho de página aplicado quando o chamador não informa <c>pageSize</c> na query string. <c>20</c> por padrão (D-09).</summary>
    [Range(1, int.MaxValue, ErrorMessage = "A chave de configuração 'Paging:DefaultPageSize' precisa ser maior que zero.")]
    public int DefaultPageSize { get; init; } = 20;

    /// <summary>Tamanho de página máximo aceito na borda. <c>100</c> por padrão (D-09) — acima disso é 400, nunca truncamento silencioso.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "A chave de configuração 'Paging:MaxPageSize' precisa ser maior que zero.")]
    public int MaxPageSize { get; init; } = 100;
}
