using Ente.Auth.Core.Abstractions;
using Ente.Auth.Core.Models;
using Ente.Auth.Core.Sync;
using Ente.Auth.Infrastructure.Security;
using Ente.Auth.Infrastructure.Storage;
using Ente.Auth.Infrastructure.Sync;

namespace Ente.Auth.Infrastructure.Tests;

public sealed class AuthenticatorSyncServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ente-auth-service-{Guid.NewGuid():N}");

    [Fact]
    public async Task PullsRemoteChangesBeforeUploadingLocalChanges()
    {
        Directory.CreateDirectory(_directory);
        var database = $"Data Source={Path.Combine(_directory, "auth.db")};Pooling=False";
        var protector = new ReversibleProtector();
        var repository = new SqliteOtpRepository(database, protector);
        var state = new SqliteAuthenticatorSyncStateStore(database, protector);
        var crypto = new LibsodiumEnteCryptoCodec();
        var key = crypto.GenerateKey();
        var entityCodec = new EnteAuthenticatorEntityCodec(crypto);
        var local = new OtpAccount(Guid.NewGuid(), "Local", "person", "JBSWY3DPEHPK3PXP");
        await repository.UpsertAsync(local);
        var remote = new OtpAccount(Guid.NewGuid(), "Remote", "person", "KRSXG5DSNFXGOIDB");
        var encryptedRemote = entityCodec.Encrypt(remote, key);
        var client = new FakeClient(new AuthenticatorEntityDto(
            "remote-existing", encryptedRemote.EncryptedData, encryptedRemote.Header, 10, 20, false));
        var service = new EnteAuthenticatorSyncService(client, state, entityCodec);

        var result = await service.SyncAsync(key);

        Assert.Equal(new AuthenticatorSyncResult(1, 1, 0), result);
        Assert.Equal(2, (await repository.GetAllAsync()).Count);
        Assert.Single(client.CreatedPayloads);
        Assert.Equal("Local", entityCodec.Decrypt(client.CreatedPayloads[0], key).Issuer);
        Assert.Empty(await state.GetPendingUploadsAsync());
        Assert.Equal(2, client.DiffCalls);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class ReversibleProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext) => plaintext.ToArray();
        public byte[] Unprotect(ReadOnlySpan<byte> ciphertext) => ciphertext.ToArray();
    }

    private sealed class FakeClient(AuthenticatorEntityDto initialRemote) : IEnteAuthenticatorClient
    {
        public int DiffCalls { get; private set; }
        public List<AuthenticatorEntityDto> CreatedPayloads { get; } = [];
        public Task<AuthenticatorDiffDto> GetDiffAsync(long sinceTime, int limit = 500, CancellationToken cancellationToken = default)
        {
            DiffCalls++;
            return Task.FromResult(DiffCalls == 1
                ? new AuthenticatorDiffDto([initialRemote], 20)
                : new AuthenticatorDiffDto([], 20));
        }
        public Task<AuthenticatorEntityDto> CreateEntityAsync(string encryptedData, string header, CancellationToken cancellationToken = default)
        {
            var entity = new AuthenticatorEntityDto("remote-created", encryptedData, header, 30, 30, false);
            CreatedPayloads.Add(entity);
            return Task.FromResult(entity);
        }
        public Task UpdateEntityAsync(string id, string encryptedData, string header, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteEntityAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AuthenticatorKeyDto> GetKeyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateKeyAsync(AuthenticatorKeyDto key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
