using System.Security.Cryptography;
using System.Text.Json;
using Ente.Auth.Core.Abstractions;
using Ente.Auth.Core.Models;
using Microsoft.Data.Sqlite;

namespace Ente.Auth.Infrastructure.Storage;

public sealed class SqliteOtpRepository(string connectionString, ISecretProtector protector) : IOtpRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private bool _initialized;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<OtpAccount>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var accounts = new List<OtpAccount>();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT protected_payload FROM otp_accounts";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var clearPayload = protector.Unprotect((byte[])reader[0]);
            try
            {
                var account = JsonSerializer.Deserialize<OtpAccount>(clearPayload, JsonOptions)
                    ?? throw new InvalidDataException("An authenticator entry was empty.");
                accounts.Add(account);
            }
            finally { CryptographicOperations.ZeroMemory(clearPayload); }
        }

        return accounts
            .OrderByDescending(account => account.IsPinned)
            .ThenByDescending(account => account.LastUsedAt)
            .ThenBy(account => account.Issuer, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(account => account.AccountName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task UpsertAsync(OtpAccount account, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var payload = JsonSerializer.SerializeToUtf8Bytes(account, JsonOptions);
        byte[] protectedPayload;
        try { protectedPayload = protector.Protect(payload); }
        finally { CryptographicOperations.ZeroMemory(payload); }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            BEGIN IMMEDIATE;
            INSERT INTO otp_accounts (id, protected_payload)
            VALUES ($id, $payload)
            ON CONFLICT(id) DO UPDATE SET protected_payload = excluded.protected_payload;
            INSERT INTO authenticator_sync_state (local_id, remote_id, pending_upload, remote_updated_at)
            VALUES ($id, NULL, 1, 0)
            ON CONFLICT(local_id) DO UPDATE SET pending_upload = 1;
            COMMIT;
            """;
        command.Parameters.AddWithValue("$id", account.Id.ToString());
        command.Parameters.AddWithValue("$payload", protectedPayload);
        await command.ExecuteNonQueryAsync(cancellationToken);
        CryptographicOperations.ZeroMemory(protectedPayload);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            BEGIN IMMEDIATE;
            INSERT OR IGNORE INTO authenticator_pending_deletions (remote_id)
                SELECT remote_id FROM authenticator_sync_state WHERE local_id = $id AND remote_id IS NOT NULL;
            DELETE FROM authenticator_sync_state WHERE local_id = $id;
            DELETE FROM otp_accounts WHERE id = $id;
            COMMIT;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            var builder = new SqliteConnectionStringBuilder(connectionString);
            var directory = Path.GetDirectoryName(builder.DataSource);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS otp_accounts (
                    id TEXT PRIMARY KEY NOT NULL,
                    protected_payload BLOB NOT NULL
                );
                CREATE TABLE IF NOT EXISTS authenticator_sync_state (
                    local_id TEXT PRIMARY KEY NOT NULL REFERENCES otp_accounts(id) ON DELETE CASCADE,
                    remote_id TEXT UNIQUE,
                    pending_upload INTEGER NOT NULL DEFAULT 1,
                    remote_updated_at INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS authenticator_pending_deletions (
                    remote_id TEXT PRIMARY KEY NOT NULL
                );
                CREATE TABLE IF NOT EXISTS authenticator_sync_meta (
                    singleton INTEGER PRIMARY KEY CHECK(singleton = 1),
                    cursor INTEGER NOT NULL DEFAULT 0
                );
                INSERT OR IGNORE INTO authenticator_sync_meta(singleton, cursor) VALUES(1, 0);
                INSERT OR IGNORE INTO authenticator_sync_state(local_id, pending_upload)
                    SELECT id, 1 FROM otp_accounts;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally { _gate.Release(); }
    }
}
