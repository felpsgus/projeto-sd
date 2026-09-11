using System.ComponentModel.DataAnnotations;
using System.Text;

namespace TodoList.Identity.Infrastructure.Security;

/// <summary>
/// Configuração de emissão/validação de access token JWT (BE-08, D-02,
/// D-31), seção <c>Jwt</c>, validada na inicialização — mesmo padrão de
/// <see cref="PasswordHashingOptions"/> (BE-06). <see cref="SigningKey"/>
/// nunca é versionada: o <c>appsettings.json</c> do Identity declara
/// <see cref="Issuer"/>/<see cref="Audience"/>, mas não a chave (ver README,
/// seção "Configuração").
/// </summary>
public sealed class JwtOptions : IValidatableObject
{
    public const string SectionName = "Jwt";

    /// <summary>Tamanho mínimo de <see cref="SigningKey"/>, em bytes UTF-8 (CA-03 de BE-08).</summary>
    public const int MinimumSigningKeyLengthInBytes = 32;

    public const int DefaultAccessTokenMinutes = 15;

    public const int DefaultRefreshTokenDays = 7;

    [Required(ErrorMessage = "Jwt:Issuer é obrigatório.")]
    public string Issuer { get; init; } = string.Empty;

    [Required(ErrorMessage = "Jwt:Audience é obrigatório.")]
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Chave simétrica de assinatura HS256. Obrigatória e com no mínimo
    /// <see cref="MinimumSigningKeyLengthInBytes"/> bytes em UTF-8 — validado
    /// em <see cref="Validate"/>, nunca por <c>[Required]</c>/<c>[MinLength]</c>
    /// simples, para a mensagem de erro nomear só a chave de configuração
    /// (<c>Jwt:SigningKey</c>), nunca o valor recebido.
    /// </summary>
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>Duração do access token, em minutos (D-02). Padrão 15, faixa 1–60 (RN-AUTH-11).</summary>
    [Range(1, 60, ErrorMessage = "Jwt:AccessTokenMinutes deve estar entre 1 e 60.")]
    public int AccessTokenMinutes { get; init; } = DefaultAccessTokenMinutes;

    /// <summary>Duração do refresh token, em dias (D-10) — usado pela futura BE-10; não emitido nesta etapa.</summary>
    [Range(1, 90, ErrorMessage = "Jwt:RefreshTokenDays deve estar entre 1 e 90.")]
    public int RefreshTokenDays { get; init; } = DefaultRefreshTokenDays;

    /// <summary>
    /// CA-02/CA-03: <see cref="SigningKey"/> ausente ou menor que
    /// <see cref="MinimumSigningKeyLengthInBytes"/> bytes falha a
    /// inicialização com uma mensagem que nomeia <c>Jwt:SigningKey</c> — o
    /// valor da chave nunca aparece na mensagem.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrEmpty(SigningKey))
        {
            yield return new ValidationResult(
                "Jwt:SigningKey é obrigatória (variável de ambiente/secret — nunca versionada).",
                [nameof(SigningKey)]);
            yield break;
        }

        if (Encoding.UTF8.GetByteCount(SigningKey) < MinimumSigningKeyLengthInBytes)
        {
            yield return new ValidationResult(
                $"Jwt:SigningKey deve ter no mínimo {MinimumSigningKeyLengthInBytes} bytes em UTF-8.",
                [nameof(SigningKey)]);
        }
    }
}
