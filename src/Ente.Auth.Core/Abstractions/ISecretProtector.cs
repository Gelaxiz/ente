namespace Ente.Auth.Core.Abstractions;

public interface ISecretProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);
    byte[] Unprotect(ReadOnlySpan<byte> ciphertext);
}
