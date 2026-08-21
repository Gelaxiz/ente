using System.Text.Json.Serialization;

namespace Ente.Auth.Core.Sync;

public sealed record AuthenticatorKeyDto(
    [property: JsonPropertyName("encryptedKey")] string EncryptedKey,
    [property: JsonPropertyName("header")] string Header);

public sealed record AuthenticatorEntityDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("encryptedData")] string? EncryptedData,
    [property: JsonPropertyName("header")] string? Header,
    [property: JsonPropertyName("createdAt")] long CreatedAt,
    [property: JsonPropertyName("updatedAt")] long UpdatedAt,
    [property: JsonPropertyName("isDeleted")] bool IsDeleted);

public sealed record AuthenticatorDiffDto(
    [property: JsonPropertyName("diff")] IReadOnlyList<AuthenticatorEntityDto> Diff,
    [property: JsonPropertyName("timestamp")] long? Timestamp);
