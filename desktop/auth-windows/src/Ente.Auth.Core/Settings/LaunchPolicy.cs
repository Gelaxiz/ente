namespace Ente.Auth.Core.Settings;

public static class LaunchPolicy
{
    public static LaunchDisposition Resolve(bool startedByWindows, LaunchMode configuredMode)
    {
        if (!startedByWindows) return LaunchDisposition.ShowWindow;

        return configuredMode switch
        {
            LaunchMode.ShowWindow => LaunchDisposition.ShowWindow,
            LaunchMode.StartMinimized => LaunchDisposition.ShowMinimized,
            LaunchMode.TrayOnly => LaunchDisposition.KeepInTray,
            _ => LaunchDisposition.ShowWindow,
        };
    }
}
