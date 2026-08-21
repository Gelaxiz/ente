using System.Security.Cryptography;
using System.Text;
using Ente.Auth.Core.Abstractions;
using Ente.Auth.Core.Models;
using Ente.Auth.Core.Settings;
using Ente.Auth.Infrastructure.Storage;
using Ente.Auth.Infrastructure.Auth;
using Ente.Auth.Core.Auth;

namespace Ente.Auth.Infrastructure.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ente-auth-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Settings_round_trip_all_launch_modes()
    {
        var store = new JsonAppSettingsStore(Path.Combine(_directory, "settings.json"));
        var expected = new AppSettings(LaunchMode.TrayOnly, true, 45, false);
        await store.SaveAsync(expected);
        Assert.Equal(expected, await store.LoadAsync());
    }

    [Fact]
    public async Task Repository_round_trip_does_not_store_plaintext_secret()
    {
        Directory.CreateDirectory(_directory);
        var databasePath = Path.Combine(_directory, "auth.db");
        var repository = new SqliteOtpRepository($"Data Source={databasePath}", new TestProtector());
        var account = new OtpAccount(Guid.NewGuid(), "Example", "alice", "JBSWY3DPEHPK3PXP", IsPinned: true);
        await repository.UpsertAsync(account);

        var restored = Assert.Single(await repository.GetAllAsync());
        Assert.Equal(account, restored);
        var rawDatabase = await File.ReadAllBytesAsync(databasePath);
        var databaseText = Encoding.UTF8.GetString(rawDatabase);
        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", databaseText);
        Assert.DoesNotContain("Example", databaseText);
        Assert.DoesNotContain("alice", databaseText);
    }

    [Fact]
    public async Task Session_round_trip_protects_token_and_account_keys()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "session.bin");
        var store = new DpapiEnteSessionStore(path, new TestProtector());
        var expected = new EnteSession("person@example.test", 42, "private-auth-token",
            Enumerable.Range(0, 32).Select(i => (byte)i).ToArray(),
            Enumerable.Range(32, 32).Select(i => (byte)i).ToArray());

        await store.SaveAsync(expected);
        var restored = await store.LoadAsync();

        Assert.Equal(expected.Email, restored?.Email);
        Assert.Equal(expected.AuthToken, restored?.AuthToken);
        Assert.Equal(expected.MasterKey, restored?.MasterKey);
        var raw = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path));
        Assert.DoesNotContain(expected.Email, raw);
        Assert.DoesNotContain(expected.AuthToken, raw);
        await store.ClearAsync();
        Assert.False(File.Exists(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        GC.SuppressFinalize(this);
    }

    private sealed class TestProtector : ISecretProtector
    {
        private static readonly byte[] Key = SHA256.HashData("test-only-protector"u8);

        public byte[] Protect(ReadOnlySpan<byte> plaintext) => Transform(plaintext);
        public byte[] Unprotect(ReadOnlySpan<byte> ciphertext) => Transform(ciphertext);

        private static byte[] Transform(ReadOnlySpan<byte> value)
        {
            var output = value.ToArray();
            for (var index = 0; index < output.Length; index++) output[index] ^= Key[index % Key.Length];
            return output;
        }
    }
}
