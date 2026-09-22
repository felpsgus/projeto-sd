using System.ComponentModel.DataAnnotations;

namespace TodoList.Gateway.Api.Configuration;

/// <summary>
/// Seção <c>Jwt</c> do Gateway (BE-40, D-38) — só a metade pública: o Gateway
/// verifica a assinatura do token localmente (<c>AddJwtBearer</c>), nunca
/// assina. <see cref="Issuer"/>/<see cref="Audience"/> precisam casar com os
/// mesmos valores do Identity, ou nenhum token emitido por ele passaria na
/// validação. Validada na inicialização, mesmo padrão de <c>JwtOptions</c> do
/// Identity — aqui espelhado do lado que só consome a chave (nunca a gera).
/// </summary>
public sealed class JwtOptions : IValidatableObject
{
    public const string SectionName = "Jwt";

    [Required(ErrorMessage = "Jwt:Issuer é obrigatório.")]
    public string Issuer { get; init; } = string.Empty;

    [Required(ErrorMessage = "Jwt:Audience é obrigatório.")]
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Caminho de um arquivo PEM SubjectPublicKeyInfo com a chave pública RSA
    /// correspondente à chave de assinatura do Identity (D-38). Obrigatório —
    /// validado em <see cref="Validate"/>, nunca por <c>[Required]</c>
    /// simples, para a mensagem de erro nomear só a chave de configuração
    /// (<c>Jwt:PublicKeyPath</c>), nunca o conteúdo do arquivo. Um caminho
    /// relativo é resolvido contra o <c>ContentRootPath</c> do host — não
    /// contra o diretório corrente do processo, que varia conforme quem
    /// inicia o Gateway (<c>dotnet run</c> de uma pasta qualquer, IDE,
    /// systemd com <c>WorkingDirectory</c> próprio) — por um
    /// <c>PostConfigure&lt;JwtOptions&gt;</c> registrado em
    /// <see cref="Authentication.ServiceCollectionExtensions"/> (a única
    /// camada que recebe <c>IHostEnvironment</c>, a partir de
    /// <c>Program.cs</c>). Por isso a propriedade tem <c>set</c>, não
    /// <c>init</c> — mesma exceção à convenção de Options já adotada no
    /// Identity para o mesmo motivo.
    /// </summary>
    [Required(ErrorMessage = "Jwt:PublicKeyPath é obrigatório.")]
    public string PublicKeyPath { get; set; } = string.Empty;

    /// <summary>
    /// CA-16: <see cref="PublicKeyPath"/> ausente, apontando para um arquivo
    /// inexistente, ilegível ou não parseável como PEM SubjectPublicKeyInfo
    /// de chave pública RSA falha a inicialização com uma mensagem que nomeia
    /// <c>Jwt:PublicKeyPath</c> — nunca o conteúdo do arquivo, nem o caminho
    /// resolvido. Sem checagem de tamanho mínimo de chave (diferente do
    /// Identity): o Gateway só consome a chave, não a gera — se ela for
    /// fraca, a falha já teria ocorrido na inicialização do Identity (D-38).
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrEmpty(PublicKeyPath))
        {
            yield return new ValidationResult(
                "Jwt:PublicKeyPath é obrigatório (caminho de um arquivo PEM SubjectPublicKeyInfo de chave pública RSA).",
                [nameof(PublicKeyPath)]);
            yield break;
        }

        var (rsa, failure) = RsaPublicKeyLoader.TryLoad(PublicKeyPath);

        using (rsa)
        {
            switch (failure)
            {
                case RsaPublicKeyLoadFailure.None:
                    break;

                case RsaPublicKeyLoadFailure.FileNotFound:
                    yield return new ValidationResult(
                        "Jwt:PublicKeyPath aponta para um arquivo que não existe.",
                        [nameof(PublicKeyPath)]);
                    break;

                case RsaPublicKeyLoadFailure.Unreadable:
                    yield return new ValidationResult(
                        "Jwt:PublicKeyPath aponta para um arquivo que não pôde ser lido (permissão).",
                        [nameof(PublicKeyPath)]);
                    break;

                case RsaPublicKeyLoadFailure.InvalidPem:
                default:
                    yield return new ValidationResult(
                        "Jwt:PublicKeyPath não aponta para um PEM SubjectPublicKeyInfo de chave pública RSA válido.",
                        [nameof(PublicKeyPath)]);
                    break;
            }
        }
    }
}
