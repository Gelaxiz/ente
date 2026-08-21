namespace Ente.Auth.Core.Abstractions;

public interface IEnteCryptoCodec
{
    byte[] GenerateKey();
    EnteCiphertext EncryptData(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key);
    byte[] DecryptData(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> header);
    EnteCiphertext WrapKey(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key);
    byte[] UnwrapKey(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce);
    byte[] DeriveArgon2IdKey(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, long memoryLimit, long operationsLimit);
    byte[] DeriveLoginKey(ReadOnlySpan<byte> keyEncryptionKey);
    byte[] OpenSealedBox(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> secretKey);
}

public sealed record EnteCiphertext(byte[] Data, byte[] Header);
