namespace Ente.Auth.Core.Sync;

public sealed record AuthenticatorSyncResult(int Downloaded, int Uploaded, int Deleted);
