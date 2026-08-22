using Ente.Auth.App.ViewModels;
using H.NotifyIcon;
using Microsoft.UI.Windowing;

namespace Ente.Auth.App;

public sealed partial class QuickPanelWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public QuickPanelWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        Root.DataContext = viewModel;
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Resize(WindowSizing.ToPixels(this, 380, 560));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                _timer.Stop();
                this.Hide();
            }
        };
        _timer.Tick += (_, _) => ((MainViewModel)Root.DataContext).Tick();
    }

    public async Task PrepareAsync()
    {
        await ((MainViewModel)Root.DataContext).ReloadAsync();
        _timer.Start();
        WindowSizing.PlaceAtWorkAreaBottomRight(this, 380, 560, 16);
        SearchBox.Focus(FocusState.Programmatic);
    }

    private async void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: OtpCodeViewModel code })
        {
            var viewModel = (MainViewModel)Root.DataContext;
            await viewModel.CopyCommand.ExecuteAsync(code);
            if (viewModel.StatusMessage.StartsWith("Copied", StringComparison.Ordinal)) this.Hide();
        }
    }

    private void OpenApp_Click(object sender, RoutedEventArgs e)
    {
        this.Hide();
        App.ShowMainWindow();
    }

    private void LockApp_Click(object sender, RoutedEventArgs e)
    {
        this.Hide();
        App.Lock();
    }

    private void ClosePanel_Click(object sender, RoutedEventArgs e)
    {
        this.Hide();
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        _timer.Stop();
        this.Hide();
        e.Handled = true;
    }
}
