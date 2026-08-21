using System.Security.Cryptography;
using System.Text.Json;
using Ente.Auth.Core.Abstractions;
using Ente.Auth.Core.Auth;

namespace Ente.Auth.Infrastructure.Auth;

public sealed class DpapiEnteSessionStore(string path, ISecretProtector protector) : IEnteSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<EnteSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;
        var protectedPayload = await File.ReadAllBytesAsync(path, cancellationToken);
        byte[]? clear = null;
        try
        {
            clear = protector.Unprotect(protectedPayload);
            return JsonSerializer.Deserialize<EnteSession>(clear, JsonOptions)
                ?? throw new InvalidDataException("The protected Ente session is empty.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedPayload);
            if (clear is not null) CryptographicOperations.ZeroMemory(clear);
        }
    }

    public async Task SaveAsync(EnteSession session, CancellationToken cancellationToken = default)
    {
        var clear = JsonSerializer.SerializeToUtf8Bytes(session, JsonOptions);
        byte[]? protectedPayload = null;
        var temporary = path + ".tmp";
        try
        {
            protectedPayload = protector.Protect(clear);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(temporary, protectedPayload, cancellationToken);
            File.Move(temporary, path, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            if (protectedPayload is not null) CryptographicOperations.ZeroMemory(protectedPayload);
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
