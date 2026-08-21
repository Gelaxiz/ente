using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Ente.Auth.Core.Abstractions;
using Ente.Auth.Core.Auth;

namespace Ente.Auth.Infrastructure.Auth;

public sealed class EnteAccountClient(HttpClient httpClient) : IEnteAccountClient
{
    public async Task<EnteSrpAttributes> GetSrpAttributesAsync(string email, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"users/srp/attributes?email={Uri.EscapeDataString(email)}", cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<SrpAttributesEnvelope>(cancellationToken);
        return envelope?.Attributes ?? throw new InvalidDataException("Ente returned empty SRP attributes.");
    }

    public async Task<(string SessionId, string SrpB)> CreateSrpSessionAsync(
        string srpUserId, string srpA, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("users/srp/create-session", new
        {
            srpUserID = srpUserId,
            srpA,
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateSessionResponse>(cancellationToken)
            ?? throw new InvalidDataException("Ente returned an empty SRP session.");
        return (result.SessionId, result.SrpB);
    }

    public Task<EnteLoginResponse> VerifySrpSessionAsync(
        string sessionId, string srpUserId, string srpM1, CancellationToken cancellationToken = default) =>
        PostLoginAsync("users/srp/verify-session", new { sessionID = sessionId, srpUserID = srpUserId, srpM1 }, cancellationToken);

    public Task<EnteLoginResponse> VerifyTotpAsync(string sessionId, string code, CancellationToken cancellationToken = default) =>
        PostLoginAsync("users/two-factor/verify", new { sessionID = sessionId, code }, cancellationToken);

    private async Task<EnteLoginResponse> PostLoginAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EnteLoginResponse>(cancellationToken)
            ?? throw new InvalidDataException("Ente returned an empty login response.");
    }

    private sealed record SrpAttributesEnvelope([property: JsonPropertyName("attributes")] EnteSrpAttributes Attributes);
    private sealed record CreateSessionResponse(
        [property: JsonPropertyName("sessionID")] string SessionId,
        [property: JsonPropertyName("srpB")] string SrpB);
}
