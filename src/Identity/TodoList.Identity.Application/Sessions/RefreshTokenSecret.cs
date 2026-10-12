using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace TodoList.Identity.Application.Sessions;

/// <summary>
/// Geração e hash do valor opaco do refresh token (BE-10). SHA-256 basta como
/// hash (sem salt nem custo): o valor já tem 256 bits de entropia, então não
/// há dicionário a atacar — só precisa não ser reversível num dump do banco
/// (RN-AUTH-20).
/// </summary>
public static class RefreshTokenSecret
{
    /// <summary>Bytes aleatórios do valor (≥ 32, CA-19 de BE-10).</summary>
    public const int ByteLength = 32;

    /// <summary>Valor opaco novo (não é JWT), Base64Url.</summary>
    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ByteLength));

    /// <summary>SHA-256 do valor em UTF-8, hex minúsculo (64 caracteres).</summary>
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
