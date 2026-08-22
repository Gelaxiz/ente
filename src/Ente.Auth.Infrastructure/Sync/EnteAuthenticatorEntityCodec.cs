using System.Security.Cryptography;
using System.Text;
using Ente.Auth.Core.Abstractions;
using Ente.Auth.Core.Models;
using Ente.Auth.Core.Otp;
using Ente.Auth.Core.Sync;

namespace Ente.Auth.Infrastructure.Sync;

public sealed class EnteAuthenticatorEntityCodec(IEnteCryptoCodec crypto)
{
    public (string EncryptedData, string Header) Encrypt(OtpAccount account, ReadOnlySpan<byte> authenticatorKey)
    {
        var plaintext = Encoding.UTF8.GetBytes(OtpTransferCodec.ExportUri(account));
        try
        {
            var result = crypto.EncryptData(plaintext, authenticatorKey);
            return (Convert.ToBase64String(result.Data), Convert.ToBase64String(result.Header));
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public OtpAccount? Decrypt(AuthenticatorEntityDto entity, ReadOnlySpan<byte> authenticatorKey)
    {
        if (entity.IsDeleted) throw new InvalidOperationException("A deleted authenticator entity has no payload.");
        if (string.IsNullOrWhiteSpace(entity.EncryptedData) || string.IsNullOrWhiteSpace(entity.Header))
            throw new InvalidDataException("The Ente authenticator entity has no encrypted payload.");

        byte[] plaintext;
        try
        {
            plaintext = crypto.DecryptData(
                Convert.FromBase64String(entity.EncryptedData),
                authenticatorKey,
                Convert.FromBase64String(entity.Header));
        }
        catch (FormatException error) { throw new InvalidDataException("The Ente entity contains invalid Base64.", error); }

        try { return OtpAuthUriParser.Parse(Encoding.UTF8.GetString(plaintext)); }
        catch (FormatException) { return null; }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
}
