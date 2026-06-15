using System.Security.Cryptography;
using System.Text;

namespace SluiceBase.Api.Mcp;

internal static class TokenHasher
{
    // Random opaque secret, URL-safe.
    public static string Generate() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    // Stable hash for storage/lookup of opaque tokens and auth codes (hex SHA-256).
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // PKCE S256: BASE64URL(SHA256(ASCII(code_verifier))). NOTE: base64url, NOT hex — distinct from Hash().
    public static string ComputePkceS256Challenge(string codeVerifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
