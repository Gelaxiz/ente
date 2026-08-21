using Ente.Auth.Core.Models;
using Ente.Auth.Core.Otp;

namespace Ente.Auth.Core.Tests;

public sealed class OtpGeneratorTests
{
    private readonly OtpGenerator _generator = new();

    [Theory]
    [InlineData(0, "755224")]
    [InlineData(1, "287082")]
    [InlineData(2, "359152")]
    [InlineData(3, "969429")]
    [InlineData(4, "338314")]
    [InlineData(5, "254676")]
    [InlineData(6, "287922")]
    [InlineData(7, "162583")]
    [InlineData(8, "399871")]
    [InlineData(9, "520489")]
    public void Hotp_matches_rfc4226(long counter, string expected)
    {
        var account = new OtpAccount(Guid.NewGuid(), "RFC", "test",
            "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", OtpKind.Hotp, Counter: counter);
        Assert.Equal(expected, _generator.Generate(account, DateTimeOffset.UnixEpoch).Code);
    }

    [Theory]
    [InlineData(59, "94287082")]
    [InlineData(1111111109, "07081804")]
    [InlineData(1111111111, "14050471")]
    [InlineData(1234567890, "89005924")]
    [InlineData(2000000000, "69279037")]
    [InlineData(20000000000, "65353130")]
    public void Totp_matches_rfc6238_sha1(long unixSeconds, string expected)
    {
        var account = new OtpAccount(Guid.NewGuid(), "RFC", "test",
            "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", OtpKind.Totp, Digits: 8);
        Assert.Equal(expected, _generator.Generate(account, DateTimeOffset.FromUnixTimeSeconds(unixSeconds)).Code);
    }
}
