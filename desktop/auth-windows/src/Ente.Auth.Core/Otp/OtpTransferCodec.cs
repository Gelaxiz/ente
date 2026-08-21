using System.Text;
using System.Text.Json;
using Ente.Auth.Core.Models;

namespace Ente.Auth.Core.Otp;

/// <summary>Imports and exports the portable newline-delimited otpauth format.</summary>
public static class OtpTransferCodec
{
    public static IReadOnlyList<OtpAccount> Import(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var accounts = new List<OtpAccount>();
        var errors = new List<int>();
        var lines = text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            try { accounts.Add(OtpAuthUriParser.Parse(line)); }
            catch (FormatException) { errors.Add(index + 1); }
        }

        if (errors.Count > 0)
            throw new FormatException($"Invalid otpauth link on line{(errors.Count == 1 ? string.Empty : "s")} {string.Join(", ", errors)}.");
        if (accounts.Count == 0)
            throw new FormatException("The file does not contain any otpauth links.");
        return accounts;
    }

    public static string Export(IEnumerable<OtpAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var result = new StringBuilder("# Ente Auth Community portable export\n# This file contains unencrypted authentication secrets.\n");
        foreach (var account in accounts) result.AppendLine(ExportUri(account));
        return result.ToString();
    }

    public static string ExportUri(OtpAccount account)
    {
        var kind = account.Kind == OtpKind.Hotp ? "hotp" : "totp";
        var label = string.IsNullOrWhiteSpace(account.Issuer)
            ? account.AccountName
            : $"{account.Issuer}:{account.AccountName}";
        var query = new List<string>
        {
            $"secret={Uri.EscapeDataString(account.Secret)}",
            $"algorithm={account.Algorithm.ToString().ToUpperInvariant()}",
            $"digits={account.Digits}",
        };
        if (!string.IsNullOrWhiteSpace(account.Issuer)) query.Add($"issuer={Uri.EscapeDataString(account.Issuer)}");
        if (account.Kind == OtpKind.Hotp) query.Add($"counter={account.Counter}");
        else query.Add($"period={account.PeriodSeconds}");
        var display = JsonSerializer.Serialize(new
        {
            pinned = account.IsPinned,
            trashed = account.IsTrashed,
            lastUsedAt = account.LastUsedAt?.ToUnixTimeMilliseconds() * 1000 ?? 0,
            tapCount = account.TapCount,
            tags = account.Tags ?? [],
            note = account.Note ?? string.Empty,
            position = account.Position,
            iconSrc = account.IconSource,
            iconID = account.IconId,
        });
        query.Add($"codeDisplay={Uri.EscapeDataString(display)}");
        return $"otpauth://{kind}/{Uri.EscapeDataString(label)}?{string.Join('&', query)}";
    }
}
