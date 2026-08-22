using Ente.Auth.Core.Abstractions;
using Ente.Auth.Core.Sync;

namespace Ente.Auth.Infrastructure.Sync;

public sealed class EnteAuthenticatorSyncService(
    IEnteAuthenticatorClient client,
    IAuthenticatorSyncStateStore state,
    EnteAuthenticatorEntityCodec entityCodec)
{
    private const int PageSize = 500;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AuthenticatorSyncResult> SyncAsync(ReadOnlyMemory<byte> authenticatorKey, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var downloaded = await PullAsync(authenticatorKey, cancellationToken);
            var deleted = 0;
            foreach (var remoteId in await state.GetPendingDeletionsAsync(cancellationToken))
            {
                await client.DeleteEntityAsync(remoteId, cancellationToken);
                await state.MarkDeletionUploadedAsync(remoteId, cancellationToken);
                deleted++;
            }

            var uploaded = 0;
            foreach (var pending in await state.GetPendingUploadsAsync(cancellationToken))
            {
                var encrypted = entityCodec.Encrypt(pending.Account, authenticatorKey.Span);
                if (pending.RemoteId is null)
                {
                    var created = await client.CreateEntityAsync(encrypted.EncryptedData, encrypted.Header, cancellationToken);
                    await state.MarkUploadedAsync(pending.Account.Id, created.Id, created.UpdatedAt, cancellationToken);
                }
                else
                {
                    await client.UpdateEntityAsync(pending.RemoteId, encrypted.EncryptedData, encrypted.Header, cancellationToken);
                    await state.MarkUploadedAsync(pending.Account.Id, pending.RemoteId, 0, cancellationToken);
                }
                uploaded++;
            }

            if (uploaded > 0 || deleted > 0) downloaded += await PullAsync(authenticatorKey, cancellationToken);
            return new AuthenticatorSyncResult(downloaded, uploaded, deleted);
        }
        finally { _gate.Release(); }
    }

    private async Task<int> PullAsync(ReadOnlyMemory<byte> authenticatorKey, CancellationToken cancellationToken)
    {
        var downloaded = 0;
        while (true)
        {
            var cursor = await state.GetCursorAsync(cancellationToken);
            var page = await client.GetDiffAsync(cursor, PageSize, cancellationToken);
            if (page.Diff.Count == 0) break;
            var maximum = cursor;
            foreach (var entity in page.Diff)
            {
                maximum = Math.Max(maximum, entity.UpdatedAt);
                if (entity.IsDeleted) await state.ApplyRemoteDeletionAsync(entity.Id, cancellationToken);
                else
                {
                    var decrypted = entityCodec.Decrypt(entity, authenticatorKey.Span);
                    if (decrypted is not null) await state.ApplyRemoteAsync(decrypted, entity.Id, entity.UpdatedAt, cancellationToken);
                }
                downloaded++;
            }
            await state.SetCursorAsync(maximum, cancellationToken);
            if (page.Diff.Count < PageSize) break;
            if (maximum <= cursor) throw new InvalidDataException("Ente sync did not advance its cursor.");
        }
        return downloaded;
    }
}
