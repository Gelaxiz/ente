using Ente.Auth.Core.Settings;

namespace Ente.Auth.Core.Abstractions;

public interface IAppSettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
