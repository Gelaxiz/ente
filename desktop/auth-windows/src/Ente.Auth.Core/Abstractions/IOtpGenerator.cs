using Ente.Auth.Core.Models;

namespace Ente.Auth.Core.Abstractions;

public interface IOtpGenerator
{
    OtpSnapshot Generate(OtpAccount account, DateTimeOffset now);
}
