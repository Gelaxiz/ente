using Ente.Auth.Core.Models;
using Ente.Auth.Infrastructure.Storage;

namespace Ente.Auth.Infrastructure.Tests;

public sealed class SyncStateStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ente-auth-sync-{Guid.NewGuid():N}");

    [Fact]
    public async Task LocalCreateUploadAndDeleteProduceExpectedSyncQueue()
    {
        Directory.CreateDirectory(_directory);
        var database = $"Data Source={Path.Combine(_directory, "auth.db")};Pooling=False";
        var protector = new ReversibleProtector();
        var repository = new SqliteOtpRepository(database, protector);
        var sync = new SqliteAuthenticatorSyncStateStore(database, protector);
        var account = new OtpAccount(Guid.NewGuid(), "Example", "person", "JBSWY3DPEHPK3PXP");

        await repository.UpsertAsync(account);
        var pending = Assert.Single(await sync.GetPendingUploadsAsync());
        Assert.Equal(account.Id, pending.Account.Id);
        Assert.Null(pending.RemoteId);

        await sync.MarkUploadedAsync(account.Id, "remote-1", 100);
        Assert.Empty(await sync.GetPendingUploadsAsync());
        await repository.DeleteAsync(account.Id);
        Assert.Equal("remote-1", Assert.Single(await sync.GetPendingDeletionsAsync()));
        await sync.MarkDeletionUploadedAsync("remote-1");
        Assert.Empty(await sync.GetPendingDeletionsAsync());
    }

    [Fact]
    public async Task RemoteUpdateAndDeletionDoNotEchoBackToServer()
    {
        Directory.CreateDirectory(_directory);
        var database = $"Data Source={Path.Combine(_directory, "auth.db")};Pooling=False";
        var protector = new ReversibleProtector();
        var repository = new SqliteOtpRepository(database, protector);
        var sync = new SqliteAuthenticatorSyncStateStore(database, protector);
        var remote = new OtpAccount(Guid.NewGuid(), "Remote", "person", "JBSWY3DPEHPK3PXP");

        await sync.ApplyRemoteAsync(remote, "remote-2", 200);
        Assert.Empty(await sync.GetPendingUploadsAsync());
        Assert.Single(await repository.GetAllAsync());
        await sync.ApplyRemoteDeletionAsync("remote-2");
        Assert.Empty(await repository.GetAllAsync());
        Assert.Empty(await sync.GetPendingDeletionsAsync());
    }

    [Fact]
    public async Task VaultBindingAllowsSameAccountAndRejectsCrossAccountSync()
    {
        Directory.CreateDirectory(_directory);
        var sync = new SqliteAuthenticatorSyncStateStore(
            $"Data Source={Path.Combine(_directory, "auth.db")};Pooling=False", new ReversibleProtector());

        await sync.BindAccountAsync(42);
        await sync.BindAccountAsync(42);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sync.BindAccountAsync(99));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class ReversibleProtector : Ente.Auth.Core.Abstractions.ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext) => plaintext.ToArray();
        public byte[] Unprotect(ReadOnlySpan<byte> ciphertext) => ciphertext.ToArray();
    }
}
