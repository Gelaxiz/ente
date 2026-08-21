namespace Ente.Auth.Core.Models;

public sealed record OtpAccount(
    Guid Id,
    string Issuer,
    string AccountName,
    string Secret,
    OtpKind Kind = OtpKind.Totp,
    OtpAlgorithm Algorithm = OtpAlgorithm.Sha1,
    int Digits = 6,
    int PeriodSeconds = 30,
    long Counter = 0,
    bool IsPinned = false,
    DateTimeOffset? LastUsedAt = null,
    string? Note = null,
    bool IsTrashed = false,
    long TapCount = 0,
    IReadOnlyList<string>? Tags = null,
    int Position = 0,
    string IconSource = "",
    string IconId = "")
{
    public string DisplayName => string.IsNullOrWhiteSpace(Issuer) ? AccountName : Issuer;
}
