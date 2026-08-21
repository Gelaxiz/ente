using Ente.Auth.Core.Models;
using Ente.Auth.Core.Otp;

namespace Ente.Auth.Core.Tests;

public sealed class OtpAuthUriParserTests
{
    [Fact]
    public void Parses_standard_totp_uri()
    {
        var account = OtpAuthUriParser.Parse("otpauth://totp/Example:alice%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=Example&algorithm=SHA256&digits=8&period=60");
        Assert.Equal("Example", account.Issuer);
        Assert.Equal("alice@example.com", account.AccountName);
        Assert.Equal(OtpKind.Totp, account.Kind);
        Assert.Equal(OtpAlgorithm.Sha256, account.Algorithm);
        Assert.Equal(8, account.Digits);
        Assert.Equal(60, account.PeriodSeconds);
    }

    [Fact]
    public void Rejects_missing_secret() =>
        Assert.Throws<FormatException>(() => OtpAuthUriParser.Parse("otpauth://totp/Example:alice"));
}
