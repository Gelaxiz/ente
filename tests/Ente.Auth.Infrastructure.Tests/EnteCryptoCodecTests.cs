using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ente.Auth.Infrastructure.Security;
using Ente.Auth.Infrastructure.Sync;
using Ente.Auth.Core.Models;
using Ente.Auth.Core.Sync;

namespace Ente.Auth.Infrastructure.Tests;

public sealed class EnteCryptoCodecTests
{
    private readonly LibsodiumEnteCryptoCodec _codec = new();

    [Fact]
    public void SecretstreamRoundTripMatchesEnteEntityLayout()
    {
        var key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var plaintext = Encoding.UTF8.GetBytes("otpauth://totp/Example:test?secret=JBSWY3DPEHPK3PXP");
        var encrypted = _codec.EncryptData(plaintext, key);

        Assert.Equal(24, encrypted.Header.Length);
        Assert.Equal(plaintext.Length + 17, encrypted.Data.Length);
        Assert.Equal(plaintext, _codec.DecryptData(encrypted.Data, key, encrypted.Header));
    }

    [Fact]
    public void SecretstreamRejectsTampering()
    {
        var key = _codec.GenerateKey();
        var encrypted = _codec.EncryptData("secret"u8, key);
        encrypted.Data[0] ^= 1;

        Assert.Throws<CryptographicException>(() => _codec.DecryptData(encrypted.Data, key, encrypted.Header));
    }

    [Fact]
    public void SecretboxRoundTripMatchesEnteKeyWrappingLayout()
    {
        var masterKey = _codec.GenerateKey();
        var authenticatorKey = _codec.GenerateKey();
        var wrapped = _codec.WrapKey(authenticatorKey, masterKey);

        Assert.Equal(24, wrapped.Header.Length);
        Assert.Equal(48, wrapped.Data.Length);
        Assert.Equal(authenticatorKey, _codec.UnwrapKey(wrapped.Data, masterKey, wrapped.Header));
    }

    [Fact]
    public void RejectsKeysWithTheWrongSize() =>
        Assert.Throws<ArgumentException>(() => _codec.EncryptData("data"u8, new byte[31]));

    [Fact]
    public void AuthenticatorEntityRoundTripUsesBase64AndOtpAuthPlaintext()
    {
        var key = _codec.GenerateKey();
        var codec = new EnteAuthenticatorEntityCodec(_codec);
        var source = new OtpAccount(Guid.NewGuid(), "Ente", "person@example.test", "JBSWY3DPEHPK3PXP");
        var encrypted = codec.Encrypt(source, key);
        var dto = new AuthenticatorEntityDto("server-id", encrypted.EncryptedData, encrypted.Header, 1, 2, false);

        var result = codec.Decrypt(dto, key);

        Assert.Equal(source.Issuer, result.Issuer);
        Assert.Equal(source.AccountName, result.AccountName);
        Assert.Equal(source.Secret, result.Secret);
    }

    [Fact]
    public void DeletedEntitiesCannotBeDecrypted()
    {
        var codec = new EnteAuthenticatorEntityCodec(_codec);
        var deleted = new AuthenticatorEntityDto("deleted", null, null, 1, 2, true);
        Assert.Throws<InvalidOperationException>(() => codec.Decrypt(deleted, _codec.GenerateKey()));
    }

    [Fact]
    public void DecryptsFixtureProducedByPinnedEnteCryptoDartRevision()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ente_crypto_dart_981a9e0.json");
        var vector = JsonSerializer.Deserialize<InteropVector>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        })!;

        Assert.Equal("ente_crypto_dart", vector.Producer);
        Assert.Equal("981a9e0f4a227023991af3332ef0e6ab6d14a1c2", vector.Revision);
        Assert.Equal(Convert.FromBase64String(vector.EntityPlaintext), _codec.DecryptData(
            Convert.FromBase64String(vector.EntityCiphertext),
            Convert.FromBase64String(vector.EntityKey),
            Convert.FromBase64String(vector.EntityHeader)));
        Assert.Equal(Convert.FromBase64String(vector.AuthenticatorKey), _codec.UnwrapKey(
            Convert.FromBase64String(vector.WrappedKeyCiphertext),
            Convert.FromBase64String(vector.MasterKey),
            Convert.FromBase64String(vector.WrappedKeyNonce)));
    }

    [Fact]
    public void MatchesDartLoginKdfAndOpensDartSealedToken()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ente_login_crypto_dart_981a9e0.json");
        var vector = JsonSerializer.Deserialize<LoginInteropVector>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        })!;

        Assert.Equal(Convert.FromBase64String(vector.LoginKey),
            _codec.DeriveLoginKey(Convert.FromBase64String(vector.KeyEncryptionKey)));
        Assert.Equal(Convert.FromBase64String(vector.Token), _codec.OpenSealedBox(
            Convert.FromBase64String(vector.SealedToken),
            Convert.FromBase64String(vector.PublicKey),
            Convert.FromBase64String(vector.SecretKey)));
    }

    private sealed record InteropVector(
        string Producer,
        string Revision,
        string EntityKey,
        string EntityPlaintext,
        string EntityHeader,
        string EntityCiphertext,
        string MasterKey,
        string AuthenticatorKey,
        string WrappedKeyNonce,
        string WrappedKeyCiphertext);

    private sealed record LoginInteropVector(
        string KeyEncryptionKey,
        string LoginKey,
        string PublicKey,
        string SecretKey,
        string SealedToken,
        string Token);
}
