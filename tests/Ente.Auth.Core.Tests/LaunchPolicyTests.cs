using Ente.Auth.Core.Settings;

namespace Ente.Auth.Core.Tests;

public sealed class LaunchPolicyTests
{
    [Theory]
    [InlineData(LaunchMode.ShowWindow, LaunchDisposition.ShowWindow)]
    [InlineData(LaunchMode.StartMinimized, LaunchDisposition.ShowMinimized)]
    [InlineData(LaunchMode.TrayOnly, LaunchDisposition.KeepInTray)]
    public void WindowsStartupHonorsConfiguredMode(LaunchMode mode, LaunchDisposition expected) =>
        Assert.Equal(expected, LaunchPolicy.Resolve(startedByWindows: true, mode));

    [Theory]
    [InlineData(LaunchMode.ShowWindow)]
    [InlineData(LaunchMode.StartMinimized)]
    [InlineData(LaunchMode.TrayOnly)]
    public void ExplicitLaunchAlwaysShowsWindow(LaunchMode mode) =>
        Assert.Equal(LaunchDisposition.ShowWindow, LaunchPolicy.Resolve(startedByWindows: false, mode));

    [Fact]
    public void UnknownPersistedModeFailsSafeToVisibleWindow() =>
        Assert.Equal(LaunchDisposition.ShowWindow, LaunchPolicy.Resolve(true, (LaunchMode)99));
}
