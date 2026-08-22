namespace Ente.Auth.Core.Settings;

public sealed record AppSettings(
    LaunchMode LaunchMode = LaunchMode.ShowWindow,
    bool LaunchAtSignIn = false,
    int ClipboardClearSeconds = 30,
    bool UseSystemTheme = true,
    bool AppLockEnabled = true,
    bool FocusSearchOnOpen = true,
    bool GridViewLayout = false,
    bool AutoSyncOnNetwork = false);
