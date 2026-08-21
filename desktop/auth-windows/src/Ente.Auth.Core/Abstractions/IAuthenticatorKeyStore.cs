namespace Ente.Auth.Core.Abstractions;

public interface IAuthenticatorKeyStore
{
    Task<byte[]?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ReadOnlyMemory<byte> key, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
