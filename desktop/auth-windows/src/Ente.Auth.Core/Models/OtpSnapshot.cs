namespace Ente.Auth.Core.Models;

public sealed record OtpSnapshot(string Code, int SecondsRemaining, double Progress, long Counter);
