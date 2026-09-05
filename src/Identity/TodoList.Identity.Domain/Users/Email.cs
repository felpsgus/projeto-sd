using System.Text.RegularExpressions;
using TodoList.SharedKernel;

namespace TodoList.Identity.Domain.Users;

/// <summary>
/// Value object de e-mail (BE-04, RN-AUTH-03). Normalizado (trim + lowercase)
/// e validado na criação — <see cref="Create"/> devolve <see cref="Result{TValue}"/>,
/// nunca lança. Imutável de forma <b>estrutural</b> (RN-USER-03): não existe
/// nenhum setter, público ou privado, nem método de alteração — o único jeito
/// de obter um <see cref="Email"/> diferente é criar outro via <see cref="Create"/>
/// (CA-08 de BE-04).
///
/// <para>
/// <b>Por que não é um <c>record</c>.</b> Um <c>record</c> geraria automaticamente
/// os operadores <c>==</c>/<c>!=</c> como chamadas de método customizadas — o que
/// complicaria a tradução de <c>Where(u =&gt; u.Email == email)</c> pelo EF Core
/// contra a coluna convertida (<c>HasConversion</c>, nota técnica de BE-04). Uma
/// classe simples com <see cref="IEquatable{T}"/> (sem sobrecarregar operadores)
/// entrega a igualdade estrutural exigida pelo CA-03 sem esse risco: o EF traduz
/// a comparação como <c>Equal</c> "crua" entre a coluna e o valor convertido.
/// </para>
/// </summary>
public sealed partial class Email : IEquatable<Email>
{
    /// <summary>254 caracteres — limite prático usual de e-mail (RFC 5321/5322), exigido por CA-01.</summary>
    public const int MaxLength = 254;

    public string Value { get; }

    private Email(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Cria um <see cref="Email"/> normalizado (trim + lowercase, CA-02) a partir de
    /// <paramref name="rawEmail"/>. Rejeita vazio, sem "@", sem domínio (sem "." após
    /// o "@"), com espaço interno e mais de <see cref="MaxLength"/> caracteres (CA-01)
    /// — sempre como <see cref="Result{TValue}"/> de falha, nunca lançando.
    /// </summary>
    public static Result<Email> Create(string? rawEmail)
    {
        if (string.IsNullOrWhiteSpace(rawEmail))
        {
            return Result.Failure<Email>(UserErrors.EmailEmpty);
        }

        var trimmed = rawEmail.Trim();

        if (trimmed.Length > MaxLength)
        {
            return Result.Failure<Email>(UserErrors.EmailTooLong);
        }

        if (!EmailFormatRegex().IsMatch(trimmed))
        {
            return Result.Failure<Email>(UserErrors.EmailInvalidFormat);
        }

        return Result.Success(new Email(trimmed.ToLowerInvariant()));
    }

    public bool Equals(Email? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as Email);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;

    // Local sem "@"/espaço, "@", domínio sem "@"/espaço com pelo menos um "."
    // (exige domínio "com TLD" — só "user@localhost" não passa, de propósito:
    // CA-01 pede rejeitar e-mail "sem domínio").
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailFormatRegex();
}
