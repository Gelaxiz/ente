using H.NotifyIcon;
using Windows.System;

namespace Ente.Auth.App;

public sealed partial class PresenceFailureWindow : Window
{
    public PresenceFailureWindow()
    {
        InitializeComponent();
        Title = "Ente Auth Community";
        AppWindow.IsShownInSwitchers = true;
        AppWindow.Resize(WindowSizing.ToPixels(this, 420, 220));
    }

    public void ShowNearTray()
    {
        WindowSizing.PlaceAtWorkAreaBottomRight(this, 420, 220, 16);
        this.Show();
        Activate();
    }

    private async void Settings_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("ms-settings:signinoptions"));

    private void Close_Click(object sender, RoutedEventArgs e) => this.Hide();
}
