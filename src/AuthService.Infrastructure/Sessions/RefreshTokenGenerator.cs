using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace AuthService.Infrastructure.Sessions;

public static class RefreshTokenGenerator
{
    public static string Generate() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return WebEncoders.Base64UrlEncode(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
    }
}
