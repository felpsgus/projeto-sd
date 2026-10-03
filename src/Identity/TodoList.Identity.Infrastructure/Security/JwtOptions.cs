using System.ComponentModel.DataAnnotations;

namespace TodoList.Identity.Infrastructure.Security;

/// <summary>
/// Configuração de emissão/validação de access token JWT (BE-08, D-02,
/// D-38), seção <c>Jwt</c>, validada na inicialização — mesmo padrão de
/// <see cref="PasswordHashingOptions"/> (BE-06). RS256 substitui o desenho
/// HS256 original (D-31): a chave privada em <see cref="PrivateKeyPath"/>
/// nunca é versionada, só o caminho do arquivo PEM é configuração — e nem
/// esse caminho aparece em <c>appsettings.json</c> compartilhado (ver
/// README, seção "Configuração").
/// </summary>
public sealed class JwtOptions : IValidatableObject
{
    public const string SectionName = "Jwt";

    /// <summary>Tamanho mínimo da chave RSA, em bits (CA-06 de BE-40).</summary>
    public const int MinimumKeySizeInBits = 2048;

    public const int DefaultAccessTokenMinutes = 15;

    public const int DefaultRefreshTokenDays = 7;

    [Required(ErrorMessage = "Jwt:Issuer é obrigatório.")]
    public string Issuer { get; init; } = string.Empty;

    [Required(ErrorMessage = "Jwt:Audience é obrigatório.")]
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Caminho de um arquivo PEM PKCS#8 com a chave privada RSA (D-38).
    /// Obrigatório — validado em <see cref="Validate"/>, nunca por
    /// <c>[Required]</c> simples, para a mensagem de erro nomear só a chave
    /// de configuração (<c>Jwt:PrivateKeyPath</c>), nunca o conteúdo do
    /// arquivo. Um caminho relativo é resolvido contra o
    /// <c>ContentRootPath</c> do host — não contra o diretório corrente —
    /// por um <c>PostConfigure&lt;JwtOptions&gt;</c> registrado em
    /// <c>Program.cs</c> (a única camada que conhece
    /// <c>IHostEnvironment</c>). Por isso a propriedade tem <c>set</c>, não
    /// <c>init</c>: é a exceção a essa convenção nas Options do Identity.
    /// </summary>
    [Required(ErrorMessage = "Jwt:PrivateKeyPath é obrigatório.")]
    public string PrivateKeyPath { get; set; } = string.Empty;

    /// <summary>Duração do access token, em minutos (D-02). Padrão 15, faixa 1–60 (RN-AUTH-11).</summary>
    [Range(1, 60, ErrorMessage = "Jwt:AccessTokenMinutes deve estar entre 1 e 60.")]
    public int AccessTokenMinutes { get; init; } = DefaultAccessTokenMinutes;

    /// <summary>Duração do refresh token, em dias (D-10, RN-AUTH-15). Padrão 7, faixa 1–90. O Gateway deriva o <c>Max-Age</c> do cookie da expiração devolvida pelo RPC, então não tem cópia desta chave.</summary>
    [Range(1, 90, ErrorMessage = "Jwt:RefreshTokenDays deve estar entre 1 e 90.")]
    public int RefreshTokenDays { get; init; } = DefaultRefreshTokenDays;

    /// <summary>
    /// CA-03 a CA-06 de BE-40: <see cref="PrivateKeyPath"/> ausente,
    /// apontando para arquivo inexistente, ilegível, não parseável como PEM
    /// PKCS8 ou com chave RSA menor que <see cref="MinimumKeySizeInBits"/>
    /// bits falha a inicialização com uma mensagem que nomeia
    /// <c>Jwt:PrivateKeyPath</c> — nunca o conteúdo do arquivo, nem o
    /// caminho resolvido.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrEmpty(PrivateKeyPath))
        {
            yield return new ValidationResult(
                "Jwt:PrivateKeyPath é obrigatório (caminho de um arquivo PEM PKCS8 de chave privada RSA).",
                [nameof(PrivateKeyPath)]);
            yield break;
        }

        var (rsa, failure) = RsaPrivateKeyLoader.TryLoad(PrivateKeyPath);

        using (rsa)
        {
            switch (failure)
            {
                case RsaPrivateKeyLoadFailure.None:
                    if (rsa!.KeySize < MinimumKeySizeInBits)
                    {
                        yield return new ValidationResult(
                            $"Jwt:PrivateKeyPath aponta para uma chave RSA menor que o mínimo exigido " +
                            $"({MinimumKeySizeInBits} bits).",
                            [nameof(PrivateKeyPath)]);
                    }

                    break;

                case RsaPrivateKeyLoadFailure.FileNotFound:
                    yield return new ValidationResult(
                        "Jwt:PrivateKeyPath aponta para um arquivo que não existe.",
                        [nameof(PrivateKeyPath)]);
                    break;

                case RsaPrivateKeyLoadFailure.Unreadable:
                    yield return new ValidationResult(
                        "Jwt:PrivateKeyPath aponta para um arquivo que não pôde ser lido (permissão).",
                        [nameof(PrivateKeyPath)]);
                    break;

                case RsaPrivateKeyLoadFailure.InvalidPem:
                default:
                    yield return new ValidationResult(
                        "Jwt:PrivateKeyPath não aponta para um PEM PKCS8 de chave privada RSA válido.",
                        [nameof(PrivateKeyPath)]);
                    break;
            }
        }
    }
}
