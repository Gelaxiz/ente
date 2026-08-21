using Ente.Auth.Core.Abstractions;
using Windows.Security.Credentials.UI;

namespace Ente.Auth.App.Services;

public sealed class WindowsHelloGate : IUserPresenceGate
{
    public async Task<bool> VerifyAsync(string message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await UserConsentVerifier.CheckAvailabilityAsync() != UserConsentVerifierAvailability.Available)
                return false;
            var result = await UserConsentVerifier.RequestVerificationAsync(message);
            cancellationToken.ThrowIfCancellationRequested();
            return result == UserConsentVerificationResult.Verified;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
