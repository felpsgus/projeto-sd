using System.ComponentModel.DataAnnotations;

namespace TodoList.Tasks.Infrastructure.Identity;

/// <summary>
/// Configuração tipada do cliente gRPC do Identity (BE-27, BE-30), vinculada
/// com <c>IOptions&lt;T&gt;</c> e validada na inicialização
/// (<c>ValidateOnStart</c>) — mesmo padrão de <c>ServiceOptions</c> (BE-01,
/// CA-08). Nenhum código deste serviço contém endereço ou porta literal fora
/// dos <c>appsettings*.json</c>, que é onde este valor é lido (CA-06).
/// </summary>
public sealed class IdentityGrpcOptions
{
    public const string SectionName = "Identity";

    /// <summary>
    /// Endereço do endpoint gRPC (h2c) do Identity Service. A validação de
    /// presença (<see cref="RequiredAttribute"/>) só pega ausência/vazio —
    /// uma string presente mas não parseável como URI absoluta (ex.:
    /// "identity-service", sem esquema) passaria batido por ela e só
    /// explodiria dentro do <c>GrpcChannel</c>, na primeira chamada. A
    /// checagem de URI absoluta de verdade fica em
    /// <see cref="ServiceCollectionExtensions.AddIdentityGrpcClient"/>
    /// (<c>.Validate(...)</c>), porque é lá que dá para nomear a chave
    /// completa (<c>Identity:GrpcAddress</c>) na mensagem de erro (CA-02).
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "A chave de configuração 'Identity:GrpcAddress' é obrigatória.")]
    public string GrpcAddress { get; init; } = string.Empty;

    /// <summary>
    /// Prazo, em segundos, de toda chamada ao Identity. Sem ele o padrão é
    /// esperar indefinidamente — uma indisponibilidade do Identity vira
    /// acúmulo de requisições no Tasks (nota técnica de BE-27).
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "A chave de configuração 'Identity:GrpcTimeoutSeconds' precisa ser maior que zero.")]
    public int GrpcTimeoutSeconds { get; init; } = 2;
}
