namespace Ente.Auth.Core.Settings;

public sealed record AppSettings(
    LaunchMode LaunchMode = LaunchMode.ShowWindow,
    bool LaunchAtSignIn = false,
    int ClipboardClearSeconds = 30,
    bool UseSystemTheme = true);
