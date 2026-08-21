using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ente.Auth.Core.Abstractions;

namespace Ente.Auth.Infrastructure.Security;

/// <summary>
/// Implements the two exact primitives used by Ente Auth: XChaCha20-Poly1305
/// secretstream for entities and XSalsa20-Poly1305 secretbox for key wrapping.
/// </summary>
public sealed partial class LibsodiumEnteCryptoCodec : IEnteCryptoCodec
{
    private const int KeyBytes = 32;
    private const int StreamHeaderBytes = 24;
    private const int StreamOverheadBytes = 17;
    private const int SecretBoxNonceBytes = 24;
    private const int SecretBoxMacBytes = 16;
    private const byte FinalTag = 3;
    private const int PasswordHashSaltBytes = 16;
    private const int Argon2Id13 = 2;
    private const int BoxPublicKeyBytes = 32;
    private const int BoxSecretKeyBytes = 32;
    private const int BoxSealOverheadBytes = 48;

    static LibsodiumEnteCryptoCodec()
    {
        if (Native.sodium_init() < 0) throw new CryptographicException("libsodium could not be initialized.");
    }

    public byte[] GenerateKey()
    {
        var key = new byte[KeyBytes];
        Native.randombytes_buf(key, (nuint)key.Length);
        return key;
    }

    public EnteCiphertext EncryptData(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key)
    {
        ValidateKey(key);
        var state = new byte[checked((int)Native.crypto_secretstream_xchacha20poly1305_statebytes())];
        var header = new byte[StreamHeaderBytes];
        var keyBytes = key.ToArray();
        try
        {
            if (Native.crypto_secretstream_xchacha20poly1305_init_push(state, header, keyBytes) != 0)
                throw new CryptographicException("Could not initialize Ente data encryption.");
            var message = plaintext.ToArray();
            var cipher = new byte[message.Length + StreamOverheadBytes];
            if (Native.crypto_secretstream_xchacha20poly1305_push(state, cipher, out var length, message,
                    (ulong)message.Length, null, 0, FinalTag) != 0)
                throw new CryptographicException("Could not encrypt Ente data.");
            if (length != (ulong)cipher.Length) throw new CryptographicException("Unexpected Ente ciphertext length.");
            CryptographicOperations.ZeroMemory(message);
            return new EnteCiphertext(cipher, header);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(state);
        }
    }

    public byte[] DecryptData(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> header)
    {
        ValidateKey(key);
        if (header.Length != StreamHeaderBytes) throw new CryptographicException("Invalid Ente secretstream header.");
        if (ciphertext.Length < StreamOverheadBytes) throw new CryptographicException("Invalid Ente ciphertext.");
        var state = new byte[checked((int)Native.crypto_secretstream_xchacha20poly1305_statebytes())];
        var keyBytes = key.ToArray();
        try
        {
            if (Native.crypto_secretstream_xchacha20poly1305_init_pull(state, header.ToArray(), keyBytes) != 0)
                throw new CryptographicException("Could not initialize Ente data decryption.");
            var cipher = ciphertext.ToArray();
            var plaintext = new byte[cipher.Length - StreamOverheadBytes];
            if (Native.crypto_secretstream_xchacha20poly1305_pull(state, plaintext, out var length, out var tag,
                    cipher, (ulong)cipher.Length, null, 0) != 0 || tag != FinalTag)
                throw new CryptographicException("Ente data authentication failed.");
            if (length != (ulong)plaintext.Length) throw new CryptographicException("Unexpected Ente plaintext length.");
            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(state);
        }
    }

    public EnteCiphertext WrapKey(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key)
    {
        ValidateKey(key);
        var nonce = new byte[SecretBoxNonceBytes];
        Native.randombytes_buf(nonce, (nuint)nonce.Length);
        var message = plaintext.ToArray();
        var keyBytes = key.ToArray();
        try
        {
            var cipher = new byte[message.Length + SecretBoxMacBytes];
            if (Native.crypto_secretbox_easy(cipher, message, (ulong)message.Length, nonce, keyBytes) != 0)
                throw new CryptographicException("Could not wrap the Ente authenticator key.");
            return new EnteCiphertext(cipher, nonce);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(message);
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    public byte[] UnwrapKey(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce)
    {
        ValidateKey(key);
        if (nonce.Length != SecretBoxNonceBytes || ciphertext.Length < SecretBoxMacBytes)
            throw new CryptographicException("Invalid Ente wrapped key.");
        var cipher = ciphertext.ToArray();
        var keyBytes = key.ToArray();
        try
        {
            var plaintext = new byte[cipher.Length - SecretBoxMacBytes];
            if (Native.crypto_secretbox_open_easy(plaintext, cipher, (ulong)cipher.Length, nonce.ToArray(), keyBytes) != 0)
                throw new CryptographicException("Ente key authentication failed.");
            return plaintext;
        }
        finally { CryptographicOperations.ZeroMemory(keyBytes); }
    }

    public byte[] DeriveArgon2IdKey(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, long memoryLimit, long operationsLimit)
    {
        if (salt.Length != PasswordHashSaltBytes) throw new ArgumentException("Argon2id salts must be 16 bytes.", nameof(salt));
        if (memoryLimit is < 8 * 1024 * 1024 or > 1024L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(memoryLimit), "Memory limit must be between 8 MiB and 1 GiB.");
        if (operationsLimit is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(operationsLimit), "Operations limit must be between 1 and 64.");
        var passwordBytes = password.ToArray();
        try
        {
            var key = new byte[KeyBytes];
            if (Native.crypto_pwhash(key, (ulong)key.Length, passwordBytes, (ulong)passwordBytes.Length,
                    salt.ToArray(), (ulong)operationsLimit, (nuint)memoryLimit, Argon2Id13) != 0)
                throw new CryptographicException("Argon2id key derivation failed.");
            return key;
        }
        finally { CryptographicOperations.ZeroMemory(passwordBytes); }
    }

    public byte[] DeriveLoginKey(ReadOnlySpan<byte> keyEncryptionKey)
    {
        ValidateKey(keyEncryptionKey);
        var fullKey = new byte[32];
        var master = keyEncryptionKey.ToArray();
        try
        {
            if (Native.crypto_kdf_derive_from_key(fullKey, (nuint)fullKey.Length, 1, "loginctx"u8.ToArray(), master) != 0)
                throw new CryptographicException("Ente login-key derivation failed.");
            return fullKey[..16];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fullKey);
            CryptographicOperations.ZeroMemory(master);
        }
    }

    public byte[] OpenSealedBox(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> secretKey)
    {
        if (publicKey.Length != BoxPublicKeyBytes) throw new ArgumentException("Box public keys must be 32 bytes.", nameof(publicKey));
        if (secretKey.Length != BoxSecretKeyBytes) throw new ArgumentException("Box secret keys must be 32 bytes.", nameof(secretKey));
        if (ciphertext.Length < BoxSealOverheadBytes) throw new CryptographicException("Invalid Ente sealed box.");
        var clear = new byte[ciphertext.Length - BoxSealOverheadBytes];
        var secret = secretKey.ToArray();
        try
        {
            if (Native.crypto_box_seal_open(clear, ciphertext.ToArray(), (ulong)ciphertext.Length, publicKey.ToArray(), secret) != 0)
                throw new CryptographicException("Ente token authentication failed.");
            return clear;
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeyBytes) throw new ArgumentException("Ente encryption keys must be 32 bytes.", nameof(key));
    }

    private static partial class Native
    {
        private const string Library = "libsodium";

        [LibraryImport(Library)] internal static partial int sodium_init();
        [LibraryImport(Library)] internal static partial void randombytes_buf([Out] byte[] buffer, nuint size);
        [LibraryImport(Library)] internal static partial nuint crypto_secretstream_xchacha20poly1305_statebytes();
        [LibraryImport(Library)] internal static partial int crypto_secretstream_xchacha20poly1305_init_push([Out] byte[] state, [Out] byte[] header, byte[] key);
        [LibraryImport(Library)] internal static partial int crypto_secretstream_xchacha20poly1305_push(byte[] state, [Out] byte[] cipher, out ulong cipherLength, byte[] message, ulong messageLength, byte[]? associatedData, ulong associatedDataLength, byte tag);
        [LibraryImport(Library)] internal static partial int crypto_secretstream_xchacha20poly1305_init_pull([Out] byte[] state, byte[] header, byte[] key);
        [LibraryImport(Library)] internal static partial int crypto_secretstream_xchacha20poly1305_pull(byte[] state, [Out] byte[] message, out ulong messageLength, out byte tag, byte[] cipher, ulong cipherLength, byte[]? associatedData, ulong associatedDataLength);
        [LibraryImport(Library)] internal static partial int crypto_secretbox_easy([Out] byte[] cipher, byte[] message, ulong messageLength, byte[] nonce, byte[] key);
        [LibraryImport(Library)] internal static partial int crypto_secretbox_open_easy([Out] byte[] message, byte[] cipher, ulong cipherLength, byte[] nonce, byte[] key);
        [LibraryImport(Library)] internal static partial int crypto_pwhash([Out] byte[] output, ulong outputLength, byte[] password, ulong passwordLength, byte[] salt, ulong operationsLimit, nuint memoryLimit, int algorithm);
        [LibraryImport(Library)] internal static partial int crypto_kdf_derive_from_key([Out] byte[] subkey, nuint subkeyLength, ulong subkeyId, byte[] context, byte[] masterKey);
        [LibraryImport(Library)] internal static partial int crypto_box_seal_open([Out] byte[] message, byte[] ciphertext, ulong ciphertextLength, byte[] publicKey, byte[] secretKey);
    }
}
