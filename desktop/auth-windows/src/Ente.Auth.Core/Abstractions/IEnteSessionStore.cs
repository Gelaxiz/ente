using Ente.Auth.Core.Auth;

namespace Ente.Auth.Core.Abstractions;

public interface IEnteSessionStore
{
    Task<EnteSession?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(EnteSession session, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
