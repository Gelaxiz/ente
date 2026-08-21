using Ente.Auth.Core.Models;

namespace Ente.Auth.Core.Sync;

public sealed record PendingAuthenticatorUpload(OtpAccount Account, string? RemoteId);
