using System.Buffers.Binary;
using System.Security.Cryptography;
using Ente.Auth.Core.Abstractions;
using Ente.Auth.Core.Encoding;
using Ente.Auth.Core.Models;

namespace Ente.Auth.Core.Otp;

public sealed class OtpGenerator : IOtpGenerator
{
    private const string SteamAlphabet = "23456789BCDFGHJKMNPQRTVWXY";

    public OtpSnapshot Generate(OtpAccount account, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);
        Validate(account);
        var unixSeconds = now.ToUnixTimeSeconds();
        var counter = account.Kind == OtpKind.Hotp ? account.Counter : unixSeconds / account.PeriodSeconds;
        var secondsRemaining = account.Kind == OtpKind.Hotp ? 0 : account.PeriodSeconds - (int)(unixSeconds % account.PeriodSeconds);
        var progress = account.Kind == OtpKind.Hotp ? 1d : secondsRemaining / (double)account.PeriodSeconds;
        var secret = Base32.Decode(account.Secret);
        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);
        byte[] digest;

        try
        {
            digest = account.Algorithm switch
            {
                OtpAlgorithm.Sha1 => HMACSHA1.HashData(secret, counterBytes),
                OtpAlgorithm.Sha256 => HMACSHA256.HashData(secret, counterBytes),
                OtpAlgorithm.Sha512 => HMACSHA512.HashData(secret, counterBytes),
                _ => throw new ArgumentOutOfRangeException(nameof(account.Algorithm)),
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }

        try
        {
            var offset = digest[^1] & 0x0f;
            var binaryCode = BinaryPrimitives.ReadInt32BigEndian(digest.AsSpan(offset, 4)) & 0x7fffffff;
            var code = account.Kind == OtpKind.Steam
                ? ToSteamCode(binaryCode)
                : (binaryCode % Pow10(account.Digits)).ToString($"D{account.Digits}");
            return new OtpSnapshot(code, secondsRemaining, progress, counter);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private static void Validate(OtpAccount account)
    {
        if (account.Kind != OtpKind.Steam && account.Digits is < 6 or > 10)
            throw new ArgumentOutOfRangeException(nameof(account), "OTP digits must be between 6 and 10.");
        if (account.Kind != OtpKind.Hotp && account.PeriodSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(account), "OTP period must be positive.");
        if (account.Counter < 0)
            throw new ArgumentOutOfRangeException(nameof(account), "HOTP counter cannot be negative.");
    }

    private static long Pow10(int exponent)
    {
        var result = 1L;
        for (var index = 0; index < exponent; index++) result *= 10;
        return result;
    }

    private static string ToSteamCode(int value)
    {
        Span<char> code = stackalloc char[5];
        for (var index = 0; index < code.Length; index++)
        {
            code[index] = SteamAlphabet[value % SteamAlphabet.Length];
            value /= SteamAlphabet.Length;
        }
        return new string(code);
    }
}
