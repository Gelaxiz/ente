using System.Security.Cryptography;
using Ente.Auth.Core.Abstractions;

namespace Ente.Auth.Infrastructure.Security;

public sealed class DpapiAuthenticatorKeyStore(string path, ISecretProtector protector) : IAuthenticatorKeyStore
{
    public async Task<byte[]?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;
        var protectedKey = await File.ReadAllBytesAsync(path, cancellationToken);
        try { return protector.Unprotect(protectedKey); }
        finally { CryptographicOperations.ZeroMemory(protectedKey); }
    }

    public async Task SaveAsync(ReadOnlyMemory<byte> key, CancellationToken cancellationToken = default)
    {
        var protectedKey = protector.Protect(key.Span);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, protectedKey, cancellationToken);
            File.Move(temporary, path, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedKey);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
