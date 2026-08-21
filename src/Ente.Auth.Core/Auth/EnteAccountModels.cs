using System.Text.Json.Serialization;

namespace Ente.Auth.Core.Auth;

public sealed record EnteSrpAttributes(
    [property: JsonPropertyName("srpUserID")] string SrpUserId,
    [property: JsonPropertyName("srpSalt")] string SrpSalt,
    [property: JsonPropertyName("memLimit")] long MemoryLimit,
    [property: JsonPropertyName("opsLimit")] long OperationsLimit,
    [property: JsonPropertyName("kekSalt")] string KekSalt,
    [property: JsonPropertyName("isEmailMFAEnabled")] bool IsEmailMfaEnabled);

public sealed record EnteKeyAttributes(
    [property: JsonPropertyName("kekSalt")] string KekSalt,
    [property: JsonPropertyName("encryptedKey")] string EncryptedKey,
    [property: JsonPropertyName("keyDecryptionNonce")] string KeyDecryptionNonce,
    [property: JsonPropertyName("publicKey")] string PublicKey,
    [property: JsonPropertyName("encryptedSecretKey")] string EncryptedSecretKey,
    [property: JsonPropertyName("secretKeyDecryptionNonce")] string SecretKeyDecryptionNonce,
    [property: JsonPropertyName("memLimit")] long MemoryLimit,
    [property: JsonPropertyName("opsLimit")] long OperationsLimit,
    [property: JsonPropertyName("masterKeyEncryptedWithRecoveryKey")] string MasterKeyEncryptedWithRecoveryKey,
    [property: JsonPropertyName("masterKeyDecryptionNonce")] string MasterKeyDecryptionNonce,
    [property: JsonPropertyName("recoveryKeyEncryptedWithMasterKey")] string RecoveryKeyEncryptedWithMasterKey,
    [property: JsonPropertyName("recoveryKeyDecryptionNonce")] string RecoveryKeyDecryptionNonce);

public sealed record EnteLoginResponse(
    [property: JsonPropertyName("id")] long? UserId,
    [property: JsonPropertyName("encryptedToken")] string? EncryptedToken,
    [property: JsonPropertyName("keyAttributes")] EnteKeyAttributes? KeyAttributes,
    [property: JsonPropertyName("twoFactorSessionID")] string? TwoFactorSessionId,
    [property: JsonPropertyName("twoFactorSessionIDV2")] string? TwoFactorSessionIdV2,
    [property: JsonPropertyName("passkeySessionID")] string? PasskeySessionId);

public sealed record EnteSession(string Email, long UserId, string AuthToken, byte[] MasterKey, byte[] SecretKey);

public abstract record EnteLoginResult
{
    public sealed record Authenticated(EnteSession Session) : EnteLoginResult;
    public sealed record TotpRequired(string SessionId) : EnteLoginResult;
    public sealed record PasskeyRequired(string SessionId) : EnteLoginResult;
}
