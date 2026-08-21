using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ente.Auth.Core.Abstractions;
using Ente.Auth.Core.Auth;
using Ente.Auth.Infrastructure.Auth;
using Ente.Auth.Infrastructure.Security;
using Org.BouncyCastle.Crypto.Agreement.Srp;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;

namespace Ente.Auth.Infrastructure.Tests;

public sealed class EnteAuthenticationServiceTests
{
    private const string Email = "person@example.test";
    private const string Password = "a strong account password";

    [Fact]
    public async Task CompletesSrpDecryptsAccountKeysAndPersistsSession()
    {
        var crypto = new LibsodiumEnteCryptoCodec();
        var client = new SrpFakeClient(crypto, requireTotp: false);
        var service = new EnteAuthenticationService(client, crypto);

        var authenticated = Assert.IsType<EnteLoginResult.Authenticated>(await service.LoginAsync(Email, Password));

        Assert.Equal(Email, authenticated.Session.Email);
        Assert.Equal(7, authenticated.Session.UserId);
        Assert.Equal(Base64Url(Encoding.UTF8.GetBytes("interop-session-token")), authenticated.Session.AuthToken);
        Assert.True(client.ProofVerified);
    }

    [Fact]
    public async Task ReturnsTotpChallengeThenCompletesWithPasswordReentry()
    {
        var crypto = new LibsodiumEnteCryptoCodec();
        var client = new SrpFakeClient(crypto, requireTotp: true);
        var service = new EnteAuthenticationService(client, crypto);

        var challenge = Assert.IsType<EnteLoginResult.TotpRequired>(await service.LoginAsync(Email, Password));
        var authenticated = Assert.IsType<EnteLoginResult.Authenticated>(
            await service.CompleteTotpAsync(Email, challenge.SessionId, "123456", Password));

        Assert.Equal(7, authenticated.Session.UserId);
    }

    [Fact]
    public async Task PrefersSupportedTotpWhenPasskeyFallbackIsAlsoAdvertised()
    {
        var crypto = new LibsodiumEnteCryptoCodec();
        var client = new SrpFakeClient(crypto, requireTotp: true, includePasskey: true);
        var service = new EnteAuthenticationService(client, crypto);

        var challenge = Assert.IsType<EnteLoginResult.TotpRequired>(await service.LoginAsync(Email, Password));

        Assert.Equal("totp-session", challenge.SessionId);
    }

    [Fact]
    public async Task WrongPasswordCannotProduceAValidSrpProof()
    {
        var crypto = new LibsodiumEnteCryptoCodec();
        var service = new EnteAuthenticationService(new SrpFakeClient(crypto, false), crypto);
        await Assert.ThrowsAsync<HttpRequestException>(() => service.LoginAsync(Email, "wrong password"));
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).Replace('+', '-').Replace('/', '_');

    private sealed class SrpFakeClient : IEnteAccountClient
    {
        private readonly bool _requireTotp;
        private readonly bool _includePasskey;
        private readonly EnteSrpAttributes _attributes;
        private readonly EnteLoginResponse _authenticatedResponse;
        private readonly BigInteger _verifier;
        private Srp6Server? _server;
        public bool ProofVerified { get; private set; }

        public SrpFakeClient(IEnteCryptoCodec crypto, bool requireTotp, bool includePasskey = false)
        {
            _requireTotp = requireTotp;
            _includePasskey = includePasskey;
            var kekSalt = Enumerable.Range(0, 16).Select(i => (byte)(32 + i)).ToArray();
            var srpSalt = Enumerable.Range(0, 16).Select(i => (byte)(64 + i)).ToArray();
            var identity = Encoding.UTF8.GetBytes("interop-srp-user");
            var passwordBytes = Encoding.UTF8.GetBytes(Password);
            var keyEncryptionKey = crypto.DeriveArgon2IdKey(passwordBytes, kekSalt, 8 * 1024 * 1024, 1);
            var loginKey = crypto.DeriveLoginKey(keyEncryptionKey);
            var group = Srp6StandardGroups.rfc5054_4096;
            var generator = new Srp6VerifierGenerator();
            generator.Init(group.N, group.G, new Sha256Digest());
            _verifier = generator.GenerateVerifier(srpSalt, identity, loginKey);
            _attributes = new EnteSrpAttributes("interop-srp-user", Convert.ToBase64String(srpSalt),
                8 * 1024 * 1024, 1, Convert.ToBase64String(kekSalt), false);

            var loginVector = JsonSerializer.Deserialize<LoginVector>(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "ente_login_crypto_dart_981a9e0.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            var masterKey = Enumerable.Range(0, 32).Select(i => (byte)(128 + i)).ToArray();
            var secretKey = Convert.FromBase64String(loginVector.SecretKey);
            var encryptedMaster = crypto.WrapKey(masterKey, keyEncryptionKey);
            var encryptedSecret = crypto.WrapKey(secretKey, masterKey);
            var keyAttributes = new EnteKeyAttributes(
                Convert.ToBase64String(kekSalt), Convert.ToBase64String(encryptedMaster.Data),
                Convert.ToBase64String(encryptedMaster.Header), loginVector.PublicKey,
                Convert.ToBase64String(encryptedSecret.Data), Convert.ToBase64String(encryptedSecret.Header),
                8 * 1024 * 1024, 1, "", "", "", "");
            _authenticatedResponse = new EnteLoginResponse(7, loginVector.SealedToken, keyAttributes, null, null, null);

            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(keyEncryptionKey);
            CryptographicOperations.ZeroMemory(loginKey);
            CryptographicOperations.ZeroMemory(masterKey);
            CryptographicOperations.ZeroMemory(secretKey);
        }

        public Task<EnteSrpAttributes> GetSrpAttributesAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(_attributes);

        public Task<(string SessionId, string SrpB)> CreateSrpSessionAsync(string srpUserId, string srpA, CancellationToken cancellationToken = default)
        {
            var group = Srp6StandardGroups.rfc5054_4096;
            _server = new Srp6Server();
            _server.Init(group.N, group.G, _verifier, new Sha256Digest(), new SecureRandom());
            var serverB = _server.GenerateServerCredentials();
            _server.CalculateSecret(new BigInteger(1, Convert.FromBase64String(srpA)));
            return Task.FromResult(("srp-session", Convert.ToBase64String(BigIntegers.AsUnsignedByteArray(serverB))));
        }

        public Task<EnteLoginResponse> VerifySrpSessionAsync(string sessionId, string srpUserId, string srpM1, CancellationToken cancellationToken = default)
        {
            ProofVerified = _server?.VerifyClientEvidenceMessage(new BigInteger(1, Convert.FromBase64String(srpM1))) == true;
            if (!ProofVerified) throw new HttpRequestException("Invalid SRP proof", null, HttpStatusCode.Unauthorized);
            return Task.FromResult(_requireTotp
                ? new EnteLoginResponse(null, null, null, "totp-session", null,
                    _includePasskey ? "passkey-session" : null)
                : _authenticatedResponse);
        }

        public Task<EnteLoginResponse> VerifyTotpAsync(string sessionId, string code, CancellationToken cancellationToken = default)
        {
            if (sessionId != "totp-session" || code != "123456")
                throw new HttpRequestException("Invalid code", null, HttpStatusCode.Unauthorized);
            return Task.FromResult(_authenticatedResponse);
        }

        private sealed record LoginVector(string PublicKey, string SecretKey, string SealedToken);
    }
}
