using System.Security.Cryptography;
using System.Text;
using Ente.Auth.Core.Abstractions;
using Ente.Auth.Core.Auth;
using Org.BouncyCastle.Crypto.Agreement.Srp;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;

namespace Ente.Auth.Infrastructure.Auth;

public sealed class EnteAuthenticationService(
    IEnteAccountClient client,
    IEnteCryptoCodec crypto)
{
    public async Task<EnteLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        email = email.Trim().ToLowerInvariant();
        if (email.Length == 0) throw new ArgumentException("Email is required.", nameof(email));
        if (password.Length == 0) throw new ArgumentException("Password is required.", nameof(password));
        var attributes = await client.GetSrpAttributesAsync(email, cancellationToken);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? keyEncryptionKey = null;
        byte[]? loginKey = null;
        try
        {
            keyEncryptionKey = crypto.DeriveArgon2IdKey(passwordBytes, Decode(attributes.KekSalt, "KEK salt"),
                attributes.MemoryLimit, attributes.OperationsLimit);
            loginKey = crypto.DeriveLoginKey(keyEncryptionKey);
            var srp = CreateSrpClient();
            var salt = Decode(attributes.SrpSalt, "SRP salt");
            var identity = Encoding.UTF8.GetBytes(attributes.SrpUserId);
            BigInteger a;
            try { a = srp.GenerateClientCredentials(salt, identity, loginKey); }
            finally
            {
                CryptographicOperations.ZeroMemory(salt);
                CryptographicOperations.ZeroMemory(identity);
            }
            var session = await client.CreateSrpSessionAsync(attributes.SrpUserId,
                Convert.ToBase64String(BigIntegers.AsUnsignedByteArray(512, a)), cancellationToken);
            var serverB = new BigInteger(1, Decode(session.SrpB, "SRP server credential"));
            srp.CalculateSecret(serverB);
            var proof = srp.CalculateClientEvidenceMessage()
                ?? throw new CryptographicException("Could not calculate the Ente SRP proof.");
            var response = await client.VerifySrpSessionAsync(session.SessionId, attributes.SrpUserId,
                Convert.ToBase64String(BigIntegers.AsUnsignedByteArray(32, proof)), cancellationToken);
            return await ProcessResponseAsync(email, response, passwordBytes, keyEncryptionKey, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (keyEncryptionKey is not null) CryptographicOperations.ZeroMemory(keyEncryptionKey);
            if (loginKey is not null) CryptographicOperations.ZeroMemory(loginKey);
        }
    }

    public async Task<EnteLoginResult> CompleteTotpAsync(
        string email, string sessionId, string code, string password, CancellationToken cancellationToken = default)
    {
        if (code.Length == 0) throw new ArgumentException("Authentication code is required.", nameof(code));
        var response = await client.VerifyTotpAsync(sessionId, code, cancellationToken);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try { return await ProcessResponseAsync(email.Trim().ToLowerInvariant(), response, passwordBytes, null, cancellationToken); }
        finally { CryptographicOperations.ZeroMemory(passwordBytes); }
    }

    private async Task<EnteLoginResult> ProcessResponseAsync(
        string email,
        EnteLoginResponse response,
        byte[] passwordBytes,
        byte[]? existingKeyEncryptionKey,
        CancellationToken cancellationToken)
    {
        var totpSession = !string.IsNullOrWhiteSpace(response.TwoFactorSessionId)
            ? response.TwoFactorSessionId
            : response.TwoFactorSessionIdV2;
        if (!string.IsNullOrWhiteSpace(totpSession)) return new EnteLoginResult.TotpRequired(totpSession);
        if (!string.IsNullOrWhiteSpace(response.PasskeySessionId))
            return new EnteLoginResult.PasskeyRequired(response.PasskeySessionId);
        if (response.UserId is null || string.IsNullOrWhiteSpace(response.EncryptedToken) || response.KeyAttributes is null)
            throw new InvalidDataException("Ente login completed without account keys or a token.");

        var attributes = response.KeyAttributes;
        byte[]? derivedKey = null;
        var keyEncryptionKey = existingKeyEncryptionKey;
        if (keyEncryptionKey is null)
        {
            derivedKey = crypto.DeriveArgon2IdKey(passwordBytes, Decode(attributes.KekSalt, "KEK salt"),
                attributes.MemoryLimit, attributes.OperationsLimit);
            keyEncryptionKey = derivedKey;
        }

        byte[]? masterKey = null;
        byte[]? secretKey = null;
        byte[]? token = null;
        try
        {
            masterKey = crypto.UnwrapKey(Decode(attributes.EncryptedKey, "encrypted master key"), keyEncryptionKey,
                Decode(attributes.KeyDecryptionNonce, "master-key nonce"));
            secretKey = crypto.UnwrapKey(Decode(attributes.EncryptedSecretKey, "encrypted secret key"), masterKey,
                Decode(attributes.SecretKeyDecryptionNonce, "secret-key nonce"));
            token = crypto.OpenSealedBox(Decode(response.EncryptedToken, "encrypted token"),
                Decode(attributes.PublicKey, "public key"), secretKey);
            var authToken = Convert.ToBase64String(token).Replace('+', '-').Replace('/', '_');
            var session = new EnteSession(email, response.UserId.Value, authToken, masterKey.ToArray(), secretKey.ToArray());
            return new EnteLoginResult.Authenticated(session);
        }
        catch (CryptographicException error)
        {
            throw new CryptographicException("The password is incorrect or the Ente account keys could not be authenticated.", error);
        }
        finally
        {
            if (derivedKey is not null) CryptographicOperations.ZeroMemory(derivedKey);
            if (masterKey is not null) CryptographicOperations.ZeroMemory(masterKey);
            if (secretKey is not null) CryptographicOperations.ZeroMemory(secretKey);
            if (token is not null) CryptographicOperations.ZeroMemory(token);
        }
    }

    private static Srp6Client CreateSrpClient()
    {
        var group = Srp6StandardGroups.rfc5054_4096;
        var client = new Srp6Client();
        client.Init(group.N, group.G, new Sha256Digest(), new SecureRandom());
        return client;
    }

    private static byte[] Decode(string value, string field)
    {
        try { return Convert.FromBase64String(value); }
        catch (FormatException error) { throw new InvalidDataException($"Ente returned invalid Base64 for {field}.", error); }
    }
}
