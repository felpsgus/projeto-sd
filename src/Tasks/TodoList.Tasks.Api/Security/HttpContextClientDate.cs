using System.Globalization;
using TodoList.Tasks.Application.Security;

namespace TodoList.Tasks.Api.Security;

/// <summary>
/// Implementação de <see cref="IClientDate"/> (BE-13, D-18) — lê
/// <c>X-Client-Date: yyyy-MM-dd</c> do request corrente. Header ausente,
/// malformado ou absurdo cai em fallback silencioso para a data UTC do
/// <see cref="TimeProvider"/> injetado — nunca erro, nunca 400 (o valor é só
/// informativo para exibição, não autoriza nem filtra nada).
/// </summary>
public sealed class HttpContextClientDate : IClientDate
{
    /// <summary>Nome do header lido para a data local do usuário (D-18).</summary>
    public const string HeaderName = "X-Client-Date";

    private const string DateFormat = "yyyy-MM-dd";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;

    public HttpContextClientDate(IHttpContextAccessor httpContextAccessor, TimeProvider timeProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
    }

    public DateOnly Today
    {
        get
        {
            var header = _httpContextAccessor.HttpContext?.Request.Headers[HeaderName].ToString();

            if (!string.IsNullOrWhiteSpace(header)
                && DateOnly.TryParseExact(header, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }

            return DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        }
    }
}
