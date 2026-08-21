using Ente.Auth.Core.Abstractions;
using Ente.Auth.Core.Sync;
using Ente.Auth.Infrastructure.Security;
using Ente.Auth.Infrastructure.Sync;

namespace Ente.Auth.Infrastructure.Tests;

public sealed class AuthenticatorKeyManagerTests
{
    [Fact]
    public async Task DownloadsUnwrapsAndCachesExistingKey()
    {
        var crypto = new LibsodiumEnteCryptoCodec();
        var master = crypto.GenerateKey();
        var expected = crypto.GenerateKey();
        var wrapped = crypto.WrapKey(expected, master);
        var client = new FakeClient(new AuthenticatorKeyDto(Convert.ToBase64String(wrapped.Data), Convert.ToBase64String(wrapped.Header)));
        var store = new MemoryKeyStore();
        var manager = new EnteAuthenticatorKeyManager(client, crypto, store);

        Assert.Equal(expected, await manager.GetOrCreateAsync(master));
        Assert.Equal(expected, await manager.GetOrCreateAsync(master));
        Assert.Equal(1, client.GetCalls);
    }

    [Fact]
    public async Task CreatesWrapsAndCachesMissingKey()
    {
        var crypto = new LibsodiumEnteCryptoCodec();
        var master = crypto.GenerateKey();
        var client = new FakeClient(null);
        var manager = new EnteAuthenticatorKeyManager(client, crypto, new MemoryKeyStore());

        var generated = await manager.GetOrCreateAsync(master);

        Assert.Equal(32, generated.Length);
        Assert.NotNull(client.Created);
        Assert.Equal(generated, crypto.UnwrapKey(
            Convert.FromBase64String(client.Created!.EncryptedKey), master,
            Convert.FromBase64String(client.Created.Header)));
    }

    private sealed class MemoryKeyStore : IAuthenticatorKeyStore
    {
        private byte[]? _value;
        public Task<byte[]?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_value?.ToArray());
        public Task SaveAsync(ReadOnlyMemory<byte> key, CancellationToken cancellationToken = default)
        {
            _value = key.ToArray();
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            _value = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClient(AuthenticatorKeyDto? existing) : IEnteAuthenticatorClient
    {
        public int GetCalls { get; private set; }
        public AuthenticatorKeyDto? Created { get; private set; }
        public Task<AuthenticatorKeyDto> GetKeyAsync(CancellationToken cancellationToken = default)
        {
            GetCalls++;
            return existing is null
                ? Task.FromException<AuthenticatorKeyDto>(new AuthenticatorKeyNotFoundException())
                : Task.FromResult(existing);
        }
        public Task CreateKeyAsync(AuthenticatorKeyDto key, CancellationToken cancellationToken = default)
        {
            Created = key;
            return Task.CompletedTask;
        }
        public Task<AuthenticatorEntityDto> CreateEntityAsync(string encryptedData, string header, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateEntityAsync(string id, string encryptedData, string header, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteEntityAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthenticatorDiffDto> GetDiffAsync(long sinceTime, int limit = 500, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
