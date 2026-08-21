using System.Security.Cryptography;
using Ente.Auth.Infrastructure.Backup;
using Ente.Auth.Infrastructure.Security;

namespace Ente.Auth.Infrastructure.Tests;

public sealed class EncryptedBackupCodecTests
{
    private readonly EnteEncryptedBackupCodec _codec = new(new LibsodiumEnteCryptoCodec());
    private const string Password = "correct horse battery staple";
    private const string Portable = "otpauth://totp/Backup:test?secret=JBSWY3DPEHPK3PXP&issuer=Backup";

    [Fact]
    public void DecryptsVersionOneBackupProducedByPinnedDartImplementation()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ente_auth_backup_v1_dart_981a9e0.json");
        Assert.Equal(Portable, _codec.Decrypt(File.ReadAllText(path), Password));
    }

    [Fact]
    public void EncryptThenDecryptRoundTripsVersionOneFormat()
    {
        var json = _codec.Encrypt(Portable, Password, 8 * 1024 * 1024, 1);
        Assert.Equal(Portable, _codec.Decrypt(json, Password));
        Assert.Contains("\"version\":1", json);
    }

    [Fact]
    public void WrongPasswordFailsClosed()
    {
        var json = _codec.Encrypt(Portable, Password, 8 * 1024 * 1024, 1);
        var error = Assert.Throws<CryptographicException>(() => _codec.Decrypt(json, "wrong password"));
        Assert.Contains("incorrect", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsUnsupportedVersion() =>
        Assert.Throws<InvalidDataException>(() => _codec.Decrypt("""
            {"version":2,"kdfParams":{"memLimit":8388608,"opsLimit":1,"salt":"oKGio6SlpqeoqaqrrK2urw=="},"encryptedData":"AA==","encryptionNonce":"AA=="}
            """, Password));

    [Fact]
    public void RejectsHostileKdfLimitsBeforeAllocatingMemory() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _codec.Decrypt("""
            {"version":1,"kdfParams":{"memLimit":9223372036854775807,"opsLimit":1,"salt":"oKGio6SlpqeoqaqrrK2urw=="},"encryptedData":"AA==","encryptionNonce":"AA=="}
            """, Password));
}
