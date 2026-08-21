using System.Text.Json;
using Ente.Auth.Core.Models;

namespace Ente.Auth.Core.Otp;

public static class OtpAuthUriParser
{
    public static OtpAccount Parse(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, "otpauth", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("The value is not a valid otpauth URI.");

        var kind = uri.Host.ToLowerInvariant() switch
        {
            "totp" => OtpKind.Totp,
            "hotp" => OtpKind.Hotp,
            _ => throw new FormatException("Only TOTP and HOTP URIs are supported."),
        };
        var query = ParseQuery(uri.Query);
        if (!query.TryGetValue("secret", out var secret) || string.IsNullOrWhiteSpace(secret))
            throw new FormatException("The otpauth URI does not contain a secret.");

        var label = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        var separator = label.IndexOf(':');
        var labelIssuer = separator >= 0 ? label[..separator].Trim() : string.Empty;
        var accountName = separator >= 0 ? label[(separator + 1)..].Trim() : label.Trim();
        var issuer = query.GetValueOrDefault("issuer", labelIssuer).Trim();
        var algorithm = query.GetValueOrDefault("algorithm", "SHA1").ToUpperInvariant() switch
        {
            "SHA1" => OtpAlgorithm.Sha1,
            "SHA256" => OtpAlgorithm.Sha256,
            "SHA512" => OtpAlgorithm.Sha512,
            _ => throw new FormatException("Unsupported OTP algorithm."),
        };

        var display = ParseDisplay(query.GetValueOrDefault("codeDisplay"));
        return new OtpAccount(Guid.NewGuid(), issuer, accountName, secret, kind, algorithm,
            ParseInt(query, "digits", 6), ParseInt(query, "period", 30), ParseLong(query, "counter", 0),
            display?.Pinned ?? false,
            display?.LastUsedAt > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(display.LastUsedAt / 1000) : null,
            display?.Note,
            display?.Trashed ?? false,
            display?.TapCount ?? 0,
            display?.Tags,
            display?.Position ?? 0,
            display?.IconSrc ?? string.Empty,
            display?.IconId ?? string.Empty);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            result[Uri.UnescapeDataString(parts[0])] = parts.Length == 2
                ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
        }
        return result;
    }

    private static int ParseInt(IReadOnlyDictionary<string, string> query, string key, int fallback) =>
        query.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    private static long ParseLong(IReadOnlyDictionary<string, string> query, string key, long fallback) =>
        query.TryGetValue(key, out var value) && long.TryParse(value, out var parsed) ? parsed : fallback;

    private static CodeDisplay? ParseDisplay(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return JsonSerializer.Deserialize<CodeDisplay>(value, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException error) { throw new FormatException("The otpauth URI contains invalid Ente display metadata.", error); }
    }

    private sealed record CodeDisplay(
        bool Pinned = false,
        bool Trashed = false,
        long LastUsedAt = 0,
        long TapCount = 0,
        IReadOnlyList<string>? Tags = null,
        string? Note = null,
        int Position = 0,
        string? IconSrc = null,
        string? IconId = null);
}
