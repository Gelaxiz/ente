using Ente.Auth.Core.Models;
using Ente.Auth.Core.Otp;

namespace Ente.Auth.Core.Tests;

public sealed class OtpTransferCodecTests
{
    [Fact]
    public void ExportThenImportPreservesOtpParameters()
    {
        var source = new OtpAccount(Guid.NewGuid(), "Example Co", "person+auth@example.test", "JBSWY3DPEHPK3PXP",
            OtpKind.Hotp, OtpAlgorithm.Sha256, 8, Counter: 42, IsPinned: true,
            LastUsedAt: DateTimeOffset.FromUnixTimeMilliseconds(1_750_000_000_123), Note: "hardware",
            Tags: ["work", "admin"], Position: 7, IconSource: "ente", IconId: "example");

        var imported = Assert.Single(OtpTransferCodec.Import(OtpTransferCodec.Export([source])));

        Assert.Equal(source.Issuer, imported.Issuer);
        Assert.Equal(source.AccountName, imported.AccountName);
        Assert.Equal(source.Secret, imported.Secret);
        Assert.Equal(source.Kind, imported.Kind);
        Assert.Equal(source.Algorithm, imported.Algorithm);
        Assert.Equal(source.Digits, imported.Digits);
        Assert.Equal(source.Counter, imported.Counter);
        Assert.Equal(source.IsPinned, imported.IsPinned);
        Assert.Equal(source.LastUsedAt, imported.LastUsedAt);
        Assert.Equal(source.Note, imported.Note);
        Assert.Equal(source.Tags, imported.Tags);
        Assert.Equal(source.Position, imported.Position);
        Assert.Equal(source.IconSource, imported.IconSource);
        Assert.Equal(source.IconId, imported.IconId);
    }

    [Fact]
    public void ImportReportsEveryInvalidLineWithoutPartiallyReturningData()
    {
        var error = Assert.Throws<FormatException>(() => OtpTransferCodec.Import("bad\n\notpauth://totp/valid?secret=JBSWY3DPEHPK3PXP\nnope"));
        Assert.Contains("1, 4", error.Message);
    }

    [Fact]
    public void ImportRejectsAnEmptyFile() =>
        Assert.Throws<FormatException>(() => OtpTransferCodec.Import("# only a comment\n"));
}
