using Ente.Auth.Core.Models;
using Ente.Auth.Core.Sync;

namespace Ente.Auth.Core.Abstractions;

public interface IAuthenticatorSyncStateStore
{
    Task BindAccountAsync(long userId, CancellationToken cancellationToken = default);
    Task<long> GetCursorAsync(CancellationToken cancellationToken = default);
    Task SetCursorAsync(long cursor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PendingAuthenticatorUpload>> GetPendingUploadsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetPendingDeletionsAsync(CancellationToken cancellationToken = default);
    Task ApplyRemoteAsync(OtpAccount account, string remoteId, long updatedAt, CancellationToken cancellationToken = default);
    Task ApplyRemoteDeletionAsync(string remoteId, CancellationToken cancellationToken = default);
    Task MarkUploadedAsync(Guid localId, string remoteId, long updatedAt, CancellationToken cancellationToken = default);
    Task MarkDeletionUploadedAsync(string remoteId, CancellationToken cancellationToken = default);
}
