using Ente.Auth.Core.Models;

namespace Ente.Auth.Core.Abstractions;

public interface IOtpRepository
{
    Task<IReadOnlyList<OtpAccount>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(OtpAccount account, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
