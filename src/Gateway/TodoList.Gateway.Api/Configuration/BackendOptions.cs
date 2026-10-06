using System.ComponentModel.DataAnnotations;

namespace TodoList.Gateway.Api.Configuration;

/// <summary>
/// Endereços e prazos dos dois backends gRPC (BE-36, CA-22/CA-23), vinculados
/// com <c>IOptions&lt;T&gt;</c> e validados na inicialização
/// (<c>ValidateOnStart</c>) — mesmo padrão de <c>IdentityGrpcOptions</c> em
/// <c>TodoList.Tasks.Infrastructure.Identity</c>. Trocar um endereço aqui
/// redireciona a chamada sem recompilar (CA-22); removê-lo, ou trocá-lo por
/// algo que não é URI absoluta, derruba a inicialização (CA-23), nunca a
/// primeira requisição.
/// </summary>
public sealed class BackendOptions
{
    public const string SectionName = "Backends";

    /// <summary>Endereço gRPC do Identity Service, aceitando <c>http://</c> (h2c local) ou <c>https://</c> (D-37).</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "A chave de configuração 'Backends:IdentityGrpcAddress' é obrigatória.")]
    public string IdentityGrpcAddress { get; init; } = string.Empty;

    /// <summary>Endereço gRPC do Tasks Service, mesmas regras de <see cref="IdentityGrpcAddress"/>.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "A chave de configuração 'Backends:TasksGrpcAddress' é obrigatória.")]
    public string TasksGrpcAddress { get; init; } = string.Empty;

    /// <summary>
    /// Prazo, em segundos, de toda chamada ao Identity — curto (padrão 2s)
    /// porque roda em toda requisição autenticada, incluindo o login.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "A chave de configuração 'Backends:IdentityGrpcTimeoutSeconds' precisa ser maior que zero.")]
    public int IdentityGrpcTimeoutSeconds { get; init; } = 2;

    /// <summary>
    /// Prazo, em segundos, de toda chamada ao Tasks — maior que o do Identity
    /// porque <c>CreateTask</c> faz, dentro do próprio Tasks, uma chamada
    /// aninhada ao Identity (<c>ValidateUser</c>) com deadline próprio de 2s;
    /// o deadline do Gateway para o Tasks precisa cobrir essa espera interna.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "A chave de configuração 'Backends:TasksGrpcTimeoutSeconds' precisa ser maior que zero.")]
    public int TasksGrpcTimeoutSeconds { get; init; } = 5;
}
