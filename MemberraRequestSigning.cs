using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Memberra.Jellyfin;

internal static class MemberraRequestSigning
{
    public static void ApplyHeaders(HttpRequestMessage request, string installToken, string body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var nonce = ToBase64Url(RandomNumberGenerator.GetBytes(18));
        var path = request.RequestUri?.AbsolutePath ?? throw new InvalidOperationException("Memberra request URI is missing.");
        var signature = CreateSignature(installToken, request.Method.Method, path, timestamp, nonce, body);
        request.Headers.TryAddWithoutValidation("X-Memberra-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-Memberra-Nonce", nonce);
        request.Headers.TryAddWithoutValidation("X-Memberra-Signature", signature);
    }

    internal static string CreateSignature(string installToken, string method, string path, string timestamp, string nonce, string body)
    {
        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var canonical = string.Join("\n", method.ToUpperInvariant(), path, timestamp, nonce, bodyHash);
        return ToBase64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(installToken), Encoding.UTF8.GetBytes(canonical)));
    }

    private static string ToBase64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
