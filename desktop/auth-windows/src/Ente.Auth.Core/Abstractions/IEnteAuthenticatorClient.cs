using Ente.Auth.Core.Sync;

namespace Ente.Auth.Core.Abstractions;

public interface IEnteAuthenticatorClient
{
    Task<AuthenticatorKeyDto> GetKeyAsync(CancellationToken cancellationToken = default);
    Task CreateKeyAsync(AuthenticatorKeyDto key, CancellationToken cancellationToken = default);
    Task<AuthenticatorEntityDto> CreateEntityAsync(string encryptedData, string header, CancellationToken cancellationToken = default);
    Task UpdateEntityAsync(string id, string encryptedData, string header, CancellationToken cancellationToken = default);
    Task DeleteEntityAsync(string id, CancellationToken cancellationToken = default);
    Task<AuthenticatorDiffDto> GetDiffAsync(long sinceTime, int limit = 500, CancellationToken cancellationToken = default);
}
