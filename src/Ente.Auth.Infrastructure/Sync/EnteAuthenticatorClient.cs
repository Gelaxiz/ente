using System.Net.Http.Json;
using Ente.Auth.Core.Abstractions;
using Ente.Auth.Core.Sync;

namespace Ente.Auth.Infrastructure.Sync;

public sealed class EnteAuthenticatorClient(HttpClient httpClient, Func<string?> authTokenProvider)
    : IEnteAuthenticatorClient
{
    public async Task<AuthenticatorKeyDto> GetKeyAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "authenticator/key");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new AuthenticatorKeyNotFoundException();
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthenticatorKeyDto>(cancellationToken)
            ?? throw new InvalidDataException("Ente returned an empty authenticator key.");
    }

    public Task CreateKeyAsync(AuthenticatorKeyDto key, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "authenticator/key", key, cancellationToken);

    public async Task<AuthenticatorEntityDto> CreateEntityAsync(string encryptedData, string header, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "authenticator/entity");
        request.Content = JsonContent.Create(new { encryptedData, header });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthenticatorEntityDto>(cancellationToken)
            ?? throw new InvalidDataException("Ente returned an empty authenticator entity.");
    }

    public Task UpdateEntityAsync(string id, string encryptedData, string header, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, "authenticator/entity", new { id, encryptedData, header }, cancellationToken);

    public Task DeleteEntityAsync(string id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"authenticator/entity?id={Uri.EscapeDataString(id)}", null, cancellationToken);

    public async Task<AuthenticatorDiffDto> GetDiffAsync(long sinceTime, int limit = 500, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"authenticator/entity/diff?sinceTime={sinceTime}&limit={limit}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthenticatorDiffDto>(cancellationToken)
            ?? new AuthenticatorDiffDto([], null);
    }

    private async Task SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var token = authTokenProvider();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("An Ente authentication token is required.");
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Auth-Token", token);
        return request;
    }
}
