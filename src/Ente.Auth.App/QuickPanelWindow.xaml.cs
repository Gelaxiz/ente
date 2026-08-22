using Ente.Auth.App.ViewModels;
using H.NotifyIcon;
using Microsoft.UI.Windowing;

namespace Ente.Auth.App;

public sealed partial class QuickPanelWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private int _focusGraceTicks;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public QuickPanelWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        Root.DataContext = viewModel;
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Resize(WindowSizing.ToPixels(this, 420, 560));
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
            else if (App.CurrentSettings.FocusSearchOnOpen)
            {
                DispatcherQueue.TryEnqueue(() => SearchBox.Focus(FocusState.Keyboard));
            }
        };
        _timer.Tick += (_, _) =>
        {
            ((MainViewModel)Root.DataContext).Tick();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (GetForegroundWindow() != hwnd)
            {
                if (_focusGraceTicks++ > 2)
                {
                    _timer.Stop();
                    this.Hide();
                }
            }
            else
            {
                _focusGraceTicks = 3;
            }
        };
    }

    public async Task PrepareAsync()
    {
        _focusGraceTicks = 0;
        await ((MainViewModel)Root.DataContext).ReloadAsync();
        _timer.Start();
        WindowSizing.PlaceAtWorkAreaBottomRight(this, 420, 560, 16);
    }

    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var viewModel = (MainViewModel)Root.DataContext;
        if (viewModel.Codes.FirstOrDefault() is { } first)
        {
            await viewModel.CopyCommand.ExecuteAsync(first);
            if (viewModel.StatusMessage.StartsWith("Copied", StringComparison.Ordinal)) this.Hide();
        }
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

    private void Code_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is OtpCodeViewModel code)
        {
            code.IsHidden = !code.IsHidden;
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
