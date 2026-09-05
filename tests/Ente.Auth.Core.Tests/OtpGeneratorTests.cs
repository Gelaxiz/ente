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

    [Fact]
    public void Totp_uses_the_same_instant_regardless_of_local_time_zone()
    {
        var account = new OtpAccount(Guid.NewGuid(), "RFC", "test",
            "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", OtpKind.Totp, Digits: 8);
        var utc = DateTimeOffset.FromUnixTimeSeconds(1_234_567_890);
        var helsinki = utc.ToOffset(TimeSpan.FromHours(3));

        Assert.Equal(_generator.Generate(account, utc), _generator.Generate(account, helsinki));
    }

    [Fact]
    public void Totp_changes_exactly_at_the_period_boundary()
    {
        var account = new OtpAccount(Guid.NewGuid(), "Boundary", "test",
            "JBSWY3DPEHPK3PXP", OtpKind.Totp, PeriodSeconds: 30);
        var justBefore = DateTimeOffset.FromUnixTimeSeconds(1_700_000_009);
        var atBoundary = justBefore.AddSeconds(1);

        var oldSnapshot = _generator.Generate(account, justBefore);
        var newSnapshot = _generator.Generate(account, atBoundary);

        Assert.Equal(1, oldSnapshot.SecondsRemaining);
        Assert.Equal(30, newSnapshot.SecondsRemaining);
        Assert.NotEqual(oldSnapshot.Counter, newSnapshot.Counter);
        Assert.NotEqual(oldSnapshot.Code, newSnapshot.Code);
    }
}
