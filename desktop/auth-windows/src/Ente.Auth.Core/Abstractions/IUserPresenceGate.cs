namespace Ente.Auth.Core.Abstractions;

public interface IUserPresenceGate
{
    Task<bool> VerifyAsync(string message, CancellationToken cancellationToken = default);
}
