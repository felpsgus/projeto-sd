using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TodoList.Identity.Application.Security;

namespace TodoList.Identity.Infrastructure.Security;

/// <summary>
/// Implementação de <see cref="IAccessTokenValidator"/> (BE-08) sobre
/// <see cref="JsonWebTokenHandler.ValidateTokenAsync(string, Microsoft.IdentityModel.Tokens.TokenValidationParameters)"/>,
/// reutilizando a fonte única de <see cref="JwtValidationParameters"/>. Sem
/// estado próprio além das dependências injetadas — thread-safe, registrado
/// como singleton (ver <see cref="ServiceCollectionExtensions"/>).
/// </summary>
public sealed partial class JwtAccessTokenValidator : IAccessTokenValidator
{
    private static readonly JsonWebTokenHandler _handler = new();

    private readonly JwtValidationParameters _validationParameters;
    private readonly ILogger<JwtAccessTokenValidator> _logger;

    public JwtAccessTokenValidator(JwtValidationParameters validationParameters, ILogger<JwtAccessTokenValidator> logger)
    {
        _validationParameters = validationParameters;
        _logger = logger;
    }

    public async Task<AccessTokenValidation> ValidateAsync(string? token, CancellationToken cancellationToken)
    {
        // CA-02/CA-07 a CA-10 de BE-08 (e CA-02/CA-03 de BE-34): nenhuma
        // entrada malformada pode lançar. Token nulo/vazio nem chega à
        // biblioteca — ValidateTokenAsync trataria como malformado de
        // qualquer forma, mas checar antes evita alocar/logar à toa.
        if (string.IsNullOrWhiteSpace(token))
        {
            return AccessTokenValidation.Invalid;
        }

        TokenValidationResult result;

        try
        {
            result = await _handler.ValidateTokenAsync(token, _validationParameters.Parameters).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // CA-13: o token em si NUNCA aparece em log — nem aqui, nem em
            // Debug. Só o fato de ter falhado e o tipo da exceção.
            Log.TokenValidationThrew(_logger, exception);

            return AccessTokenValidation.Invalid;
        }

        if (!result.IsValid)
        {
            Log.TokenRejected(_logger, result.Exception);

            return AccessTokenValidation.Invalid;
        }

        if (!result.Claims.TryGetValue(JwtRegisteredClaimNames.Sub, out var subClaim)
            || subClaim is not string subValue
            || !Guid.TryParse(subValue, out var userId))
        {
            return AccessTokenValidation.Invalid;
        }

        return AccessTokenValidation.Valid(userId);
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Falha ao validar access token (BE-08). O token em si nunca é logado (CA-13).")]
        public static partial void TokenValidationThrew(ILogger logger, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Access token rejeitado na validação (BE-08). O token em si nunca é logado (CA-13).")]
        public static partial void TokenRejected(ILogger logger, Exception? exception);
    }
}
