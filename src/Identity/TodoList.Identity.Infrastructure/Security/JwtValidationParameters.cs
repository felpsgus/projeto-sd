using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace TodoList.Identity.Infrastructure.Security;

/// <summary>
/// Fonte única dos <see cref="TokenValidationParameters"/> usados para
/// validar um access token JWT (BE-08). Construído uma vez, a partir de
/// <see cref="JwtOptions"/> e do <see cref="TimeProvider"/> do processo, e
/// registrado como singleton (ver <see cref="ServiceCollectionExtensions"/>).
///
/// <para>
/// <b>Reuso obrigatório.</b> Hoje o único consumidor é
/// <see cref="JwtAccessTokenValidator"/> (por sua vez usado por
/// <c>IdentityGrpcService.ValidateToken</c>, BE-34). Quando o Bearer entrar
/// no pipeline HTTP do próprio Identity (fora do recorte do T2, ver D-36),
/// ele <b>DEVE</b> consumir esta mesma instância — nunca recriar um segundo
/// <see cref="TokenValidationParameters"/> com issuer/audience/chave/
/// <see cref="TokenValidationParameters.ClockSkew"/> divergentes.
/// </para>
/// </summary>
public sealed class JwtValidationParameters
{
    public TokenValidationParameters Parameters { get; }

    public JwtValidationParameters(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        var jwtOptions = options.Value;
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

        Parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

            // ClockSkew padrão do framework é 5 minutos — inaceitável para um
            // token de 15 (D-02): um token "expirado" continuaria sendo
            // aceito por mais 5 minutos. Zero é exigido pelo CA-07/CA-09 de
            // BE-08 e pelo CA-04 de BE-34.
            ClockSkew = TimeSpan.Zero,

            ValidateLifetime = true,

            // Armadilha de tempo (nota técnica de BE-08/BE-34): esta versão
            // do Microsoft.IdentityModel.Tokens não expõe uma propriedade
            // "TimeProvider" em TokenValidationParameters — a checagem de
            // vida útil por padrão usa o relógio do sistema. Por isso a
            // validação de exp/nbf é feita aqui, comparando com
            // timeProvider.GetUtcNow(), para o CA-07 (BE-08) e o CA-04
            // (BE-34) serem verificáveis com FakeTimeProvider sem depender do
            // relógio real da máquina.
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;

                if (expires is null)
                {
                    return false;
                }

                if (notBefore is not null && notBefore.Value > now)
                {
                    return false;
                }

                return expires.Value > now;
            },
        };
    }
}
