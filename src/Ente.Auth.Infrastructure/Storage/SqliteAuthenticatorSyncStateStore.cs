using System.Security.Cryptography;
using System.Text.Json;
using Ente.Auth.Core.Abstractions;
using Ente.Auth.Core.Models;
using Ente.Auth.Core.Sync;
using Microsoft.Data.Sqlite;

namespace Ente.Auth.Infrastructure.Storage;

public sealed class SqliteAuthenticatorSyncStateStore(string connectionString, ISecretProtector protector)
    : IAuthenticatorSyncStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public async Task BindAccountAsync(long userId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        var query = connection.CreateCommand();
        query.CommandText = "SELECT user_id FROM authenticator_account_binding WHERE singleton = 1";
        var existing = await query.ExecuteScalarAsync(cancellationToken);
        if (existing is not null && existing is not DBNull && (long)existing != userId)
            throw new InvalidOperationException("This local vault is linked to a different Ente account. Use a separate Windows profile or reset the local vault before switching accounts.");
        var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO authenticator_account_binding(singleton, user_id) VALUES(1, $user)";
        command.Parameters.AddWithValue("$user", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> GetCursorAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT cursor FROM authenticator_sync_meta WHERE singleton = 1";
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    public async Task SetCursorAsync(long cursor, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE authenticator_sync_meta SET cursor = MAX(cursor, $cursor) WHERE singleton = 1";
        command.Parameters.AddWithValue("$cursor", cursor);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PendingAuthenticatorUpload>> GetPendingUploadsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var result = new List<PendingAuthenticatorUpload>();
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.protected_payload, s.remote_id
            FROM authenticator_sync_state s
            JOIN otp_accounts a ON a.id = s.local_id
            WHERE s.pending_upload = 1
            ORDER BY a.id
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var clear = protector.Unprotect((byte[])reader[0]);
            try
            {
                var account = JsonSerializer.Deserialize<OtpAccount>(clear, JsonOptions)
                    ?? throw new InvalidDataException("A pending authenticator entry was empty.");
                result.Add(new PendingAuthenticatorUpload(account, reader.IsDBNull(1) ? null : reader.GetString(1)));
            }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        return result;
    }

    public async Task<IReadOnlyList<string>> GetPendingDeletionsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var result = new List<string>();
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT remote_id FROM authenticator_pending_deletions ORDER BY remote_id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(0));
        return result;
    }

    public async Task ApplyRemoteAsync(OtpAccount account, string remoteId, long updatedAt, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        var existing = connection.CreateCommand();
        existing.CommandText = "SELECT local_id FROM authenticator_sync_state WHERE remote_id = $remote";
        existing.Parameters.AddWithValue("$remote", remoteId);
        var localId = (string?)await existing.ExecuteScalarAsync(cancellationToken) ?? account.Id.ToString();
        var normalized = account with { Id = Guid.Parse(localId) };
        var protectedPayload = Protect(normalized);
        try
        {
            var command = connection.CreateCommand();
            command.CommandText = """
                BEGIN IMMEDIATE;
                INSERT INTO otp_accounts(id, protected_payload) VALUES($local, $payload)
                    ON CONFLICT(id) DO UPDATE SET protected_payload = excluded.protected_payload;
                INSERT INTO authenticator_sync_state(local_id, remote_id, pending_upload, remote_updated_at)
                    VALUES($local, $remote, 0, $updated)
                    ON CONFLICT(local_id) DO UPDATE SET remote_id = excluded.remote_id,
                        pending_upload = 0, remote_updated_at = excluded.remote_updated_at;
                DELETE FROM authenticator_pending_deletions WHERE remote_id = $remote;
                COMMIT;
                """;
            command.Parameters.AddWithValue("$local", localId);
            command.Parameters.AddWithValue("$remote", remoteId);
            command.Parameters.AddWithValue("$updated", updatedAt);
            command.Parameters.AddWithValue("$payload", protectedPayload);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally { CryptographicOperations.ZeroMemory(protectedPayload); }
    }

    public async Task ApplyRemoteDeletionAsync(string remoteId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            BEGIN IMMEDIATE;
            DELETE FROM otp_accounts WHERE id IN (SELECT local_id FROM authenticator_sync_state WHERE remote_id = $remote);
            DELETE FROM authenticator_sync_state WHERE remote_id = $remote;
            DELETE FROM authenticator_pending_deletions WHERE remote_id = $remote;
            COMMIT;
            """;
        command.Parameters.AddWithValue("$remote", remoteId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkUploadedAsync(Guid localId, string remoteId, long updatedAt, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE authenticator_sync_state SET remote_id = $remote, pending_upload = 0,
                remote_updated_at = MAX(remote_updated_at, $updated) WHERE local_id = $local
            """;
        command.Parameters.AddWithValue("$local", localId.ToString());
        command.Parameters.AddWithValue("$remote", remoteId);
        command.Parameters.AddWithValue("$updated", updatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkDeletionUploadedAsync(string remoteId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM authenticator_pending_deletions WHERE remote_id = $remote";
        command.Parameters.AddWithValue("$remote", remoteId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private byte[] Protect(OtpAccount account)
    {
        var clear = JsonSerializer.SerializeToUtf8Bytes(account, JsonOptions);
        try { return protector.Protect(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await using var connection = await OpenAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS otp_accounts (id TEXT PRIMARY KEY NOT NULL, protected_payload BLOB NOT NULL);
                CREATE TABLE IF NOT EXISTS authenticator_sync_state (
                    local_id TEXT PRIMARY KEY NOT NULL, remote_id TEXT UNIQUE,
                    pending_upload INTEGER NOT NULL DEFAULT 1, remote_updated_at INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE IF NOT EXISTS authenticator_pending_deletions (remote_id TEXT PRIMARY KEY NOT NULL);
                CREATE TABLE IF NOT EXISTS authenticator_sync_meta (
                    singleton INTEGER PRIMARY KEY CHECK(singleton = 1), cursor INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE IF NOT EXISTS authenticator_account_binding (
                    singleton INTEGER PRIMARY KEY CHECK(singleton = 1), user_id INTEGER NOT NULL);
                INSERT OR IGNORE INTO authenticator_sync_meta(singleton, cursor) VALUES(1, 0);
                INSERT OR IGNORE INTO authenticator_sync_state(local_id, pending_upload) SELECT id, 1 FROM otp_accounts;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally { _gate.Release(); }
    }
}
