using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ente.Auth.Core.Abstractions;

namespace Ente.Auth.Infrastructure.Backup;

public sealed class EnteEncryptedBackupCodec(IEnteCryptoCodec crypto)
{
    public const long DefaultMemoryLimit = 268_435_456;
    public const long DefaultOperationsLimit = 16;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Encrypt(string portableOtpAuthData, string password,
        long memoryLimit = DefaultMemoryLimit, long operationsLimit = DefaultOperationsLimit)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("A backup password is required.", nameof(password));
        var salt = RandomNumberGenerator.GetBytes(16);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? key = null;
        var plaintext = Encoding.UTF8.GetBytes(portableOtpAuthData);
        try
        {
            key = crypto.DeriveArgon2IdKey(passwordBytes, salt, memoryLimit, operationsLimit);
            var encrypted = crypto.EncryptData(plaintext, key);
            return JsonSerializer.Serialize(new BackupDocument(
                1,
                new KdfParameters(memoryLimit, operationsLimit, Convert.ToBase64String(salt)),
                Convert.ToBase64String(encrypted.Data),
                Convert.ToBase64String(encrypted.Header)), JsonOptions);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(plaintext);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    public string Decrypt(string json, string password)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("A backup password is required.", nameof(password));
        BackupDocument document;
        try { document = JsonSerializer.Deserialize<BackupDocument>(json, JsonOptions) ?? throw new InvalidDataException("The backup is empty."); }
        catch (JsonException error) { throw new InvalidDataException("The file is not a valid Ente Auth backup.", error); }
        if (document.Version != 1) throw new InvalidDataException($"Ente Auth backup version {document.Version} is not supported.");
        if (document.KdfParams is null) throw new InvalidDataException("The Ente Auth backup has no KDF parameters.");

        byte[] salt;
        byte[] ciphertext;
        byte[] header;
        try
        {
            salt = Convert.FromBase64String(document.KdfParams.Salt);
            ciphertext = Convert.FromBase64String(document.EncryptedData);
            header = Convert.FromBase64String(document.EncryptionNonce);
        }
        catch (FormatException error) { throw new InvalidDataException("The Ente Auth backup contains invalid Base64.", error); }

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? key = null;
        byte[]? plaintext = null;
        try
        {
            key = crypto.DeriveArgon2IdKey(passwordBytes, salt, document.KdfParams.MemLimit, document.KdfParams.OpsLimit);
            plaintext = crypto.DecryptData(ciphertext, key, header);
            return new UTF8Encoding(false, true).GetString(plaintext);
        }
        catch (CryptographicException error) { throw new CryptographicException("The backup password is incorrect or the file was modified.", error); }
        catch (DecoderFallbackException error) { throw new InvalidDataException("The decrypted backup is not valid UTF-8.", error); }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(header);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private sealed record BackupDocument(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("kdfParams")] KdfParameters KdfParams,
        [property: JsonPropertyName("encryptedData")] string EncryptedData,
        [property: JsonPropertyName("encryptionNonce")] string EncryptionNonce);

    private sealed record KdfParameters(
        [property: JsonPropertyName("memLimit")] long MemLimit,
        [property: JsonPropertyName("opsLimit")] long OpsLimit,
        [property: JsonPropertyName("salt")] string Salt);
}
