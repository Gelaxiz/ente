using Ente.Auth.App.ViewModels;
using H.NotifyIcon;
using Microsoft.UI.Windowing;

namespace Ente.Auth.App;

public sealed partial class QuickPanelWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private int _focusGraceTicks;
    private bool _isOpening;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

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
                if (_isOpening) return;
                _timer.Stop();
                this.Hide();
            }
        };
        _timer.Tick += (_, _) =>
        {
            ((MainViewModel)Root.DataContext).Tick();
            if (_isOpening) return;
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
        _isOpening = true;
        _focusGraceTicks = 0;
        var viewModel = (MainViewModel)Root.DataContext;
        viewModel.SearchText = string.Empty;
        await viewModel.ReloadAsync();
        _timer.Start();
        WindowSizing.PlaceAtWorkAreaBottomRight(this, 420, 560, 16);
    }

    public async Task CompleteOpeningAsync()
    {
        // WinUI can report activation before the popup's visual tree is ready.
        // Retry briefly after Show/SetForegroundWindow so tray and hotkey opens
        // consistently put keyboard input in search.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            SetForegroundWindow(hwnd);
            await Task.Delay(attempt == 0 ? 40 : 80);
            if (!App.CurrentSettings.FocusSearchOnOpen) break;
            if (SearchBox.Focus(FocusState.Keyboard) && GetForegroundWindow() == hwnd) break;
        }

        _isOpening = false;
        _focusGraceTicks = GetForegroundWindow() == hwnd ? 3 : 0;
    }

    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var viewModel = (MainViewModel)Root.DataContext;
        // QuerySubmitted may run before the two-way Text binding has delivered
        // its final value. Filter explicitly before choosing the top result.
        viewModel.SearchText = args.QueryText ?? sender.Text ?? string.Empty;
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

    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        _timer.Stop();
        this.Hide();
        e.Handled = true;
    }
}
