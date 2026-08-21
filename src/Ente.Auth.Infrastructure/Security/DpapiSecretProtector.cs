using System.Runtime.Versioning;
using System.Security.Cryptography;
using Ente.Auth.Core.Abstractions;

namespace Ente.Auth.Infrastructure.Security;

[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "Ente.Auth.Community.v1"u8.ToArray();

    public byte[] Protect(ReadOnlySpan<byte> plaintext) =>
        ProtectedData.Protect(plaintext.ToArray(), Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(ReadOnlySpan<byte> ciphertext) =>
        ProtectedData.Unprotect(ciphertext.ToArray(), Entropy, DataProtectionScope.CurrentUser);
}
