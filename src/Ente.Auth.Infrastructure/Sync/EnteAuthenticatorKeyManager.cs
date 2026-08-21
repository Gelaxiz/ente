using System.Security.Cryptography;
using Ente.Auth.Core.Abstractions;
using Ente.Auth.Core.Sync;

namespace Ente.Auth.Infrastructure.Sync;

public sealed class EnteAuthenticatorKeyManager(
    IEnteAuthenticatorClient client,
    IEnteCryptoCodec crypto,
    IAuthenticatorKeyStore keyStore)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<byte[]> GetOrCreateAsync(ReadOnlyMemory<byte> accountMasterKey, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var cached = await keyStore.LoadAsync(cancellationToken);
            if (cached is { Length: 32 }) return cached;
            if (cached is not null) CryptographicOperations.ZeroMemory(cached);

            try
            {
                var remote = await client.GetKeyAsync(cancellationToken);
                byte[] key;
                try
                {
                    key = crypto.UnwrapKey(
                        Convert.FromBase64String(remote.EncryptedKey),
                        accountMasterKey.Span,
                        Convert.FromBase64String(remote.Header));
                }
                catch (FormatException error) { throw new InvalidDataException("The Ente authenticator key contains invalid Base64.", error); }
                if (key.Length != 32)
                {
                    CryptographicOperations.ZeroMemory(key);
                    throw new CryptographicException("The Ente authenticator key has an invalid length.");
                }
                await keyStore.SaveAsync(key, cancellationToken);
                return key;
            }
            catch (AuthenticatorKeyNotFoundException)
            {
                var key = crypto.GenerateKey();
                var wrapped = crypto.WrapKey(key, accountMasterKey.Span);
                await client.CreateKeyAsync(new AuthenticatorKeyDto(
                    Convert.ToBase64String(wrapped.Data), Convert.ToBase64String(wrapped.Header)), cancellationToken);
                await keyStore.SaveAsync(key, cancellationToken);
                return key;
            }
        }
        finally { _gate.Release(); }
    }
}
