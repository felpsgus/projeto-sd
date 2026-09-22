using System.ComponentModel.DataAnnotations;

namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Configuração tipada de paginação (BE-41, D-09) — vinculada com
/// <c>IOptions&lt;T&gt;</c> a partir da seção <c>Paging</c>, validada na
/// inicialização (<c>ValidateOnStart</c>, mesmo padrão de
/// <see cref="TaskOptions"/>/<c>ServiceOptions</c>). <see cref="ListTasksHandler"/>
/// é o único consumidor até esta task; BE-22, quando entrar, reaproveita o
/// mesmo tipo sem alteração (nota técnica de BE-41: "por que os limites de
/// paginação são os mesmos de BE-22, e não um valor novo").
///
/// <para>
/// Mora em <c>Application</c>, não em <c>Api</c>, pela mesma razão de
/// <see cref="TaskOptions"/>: é o próprio caso de uso (<see cref="ListTasksHandler"/>)
/// que lê o valor, não a borda gRPC.
/// </para>
/// </summary>
public sealed class PagingOptions
{
    /// <summary>Nome da seção de configuração (D-09).</summary>
    public const string SectionName = "Paging";

    /// <summary>Tamanho de página aplicado quando o chamador não informa <c>page_size</c> (ou informa 0). <c>20</c> por padrão (D-09).</summary>
    [Range(1, int.MaxValue)]
    public int DefaultPageSize { get; set; } = 20;

    /// <summary>Tamanho de página máximo aceito. <c>100</c> por padrão (D-09) — acima disso é erro de validação, nunca truncamento silencioso.</summary>
    [Range(1, int.MaxValue)]
    public int MaxPageSize { get; set; } = 100;
}
