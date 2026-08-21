using Ente.Auth.Core.Auth;

namespace Ente.Auth.Core.Abstractions;

public interface IEnteAccountClient
{
    Task<EnteSrpAttributes> GetSrpAttributesAsync(string email, CancellationToken cancellationToken = default);
    Task<(string SessionId, string SrpB)> CreateSrpSessionAsync(string srpUserId, string srpA, CancellationToken cancellationToken = default);
    Task<EnteLoginResponse> VerifySrpSessionAsync(string sessionId, string srpUserId, string srpM1, CancellationToken cancellationToken = default);
    Task<EnteLoginResponse> VerifyTotpAsync(string sessionId, string code, CancellationToken cancellationToken = default);
}
