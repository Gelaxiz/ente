using Ente.Auth.App.Services;
using Ente.Auth.App.ViewModels;
using Ente.Auth.Core.Abstractions;
using Ente.Auth.Core.Otp;
using Ente.Auth.Core.Settings;
using Ente.Auth.Core.Auth;
using Ente.Auth.Infrastructure.Auth;
using Ente.Auth.Infrastructure.Security;
using Ente.Auth.Infrastructure.Storage;
using Ente.Auth.Infrastructure.Backup;
using Ente.Auth.Infrastructure.Sync;
using System.Security.Cryptography;
using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.Storage;
using AppInstance = Microsoft.Windows.AppLifecycle.AppInstance;

namespace Ente.Auth.App;

public sealed partial class App : Application
{
    private static MainWindow? _mainWindow;
    private static QuickPanelWindow? _quickPanel;
    private static PresenceFailureWindow? _presenceFailureWindow;
    private static MainViewModel? _viewModel;
    private static MainViewModel? _quickViewModel;
    private static IAppSettingsStore? _settingsStore;
    private static IEnteSessionStore? _sessionStore;
    private static IAuthenticatorKeyStore? _authenticatorKeyStore;
    private static EnteAuthenticationService? _authenticationService;
    private static EnteAuthenticatorKeyManager? _authenticatorKeyManager;
    private static EnteAuthenticatorSyncService? _syncService;
    private static IAuthenticatorSyncStateStore? _syncStateStore;
    private static EnteSession? _session;
    private static TaskbarIcon? _trayIcon;
    private static readonly IUserPresenceGate PresenceGate = new WindowsHelloGate();
    private static readonly SemaphoreSlim PresenceGateLock = new(1, 1);
    private static readonly SemaphoreSlim SyncLifecycleGate = new(1, 1);
    private static CancellationTokenSource _syncCancellation = new();
    private static bool _isLocked;

    public static AppSettings CurrentSettings { get; private set; } = new();
    public static bool IsQuitting { get; private set; }
    public static bool IsSignedIn => _session is not null;
    public static string? SignedInEmail => _session?.Email;
    public static string? LastSyncError { get; private set; }

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var current = AppInstance.FindOrRegisterForKey("Ente.Auth.Community.SingleInstance");
        if (!current.IsCurrent)
        {
            await current.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
            Environment.Exit(0);
            return;
        }
        var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        current.Activated += (_, _) => dispatcherQueue.TryEnqueue(async () => await ShowMainWindowAsync());

        var dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EnteAuthCommunity");
        Directory.CreateDirectory(dataPath);
        _settingsStore = new JsonAppSettingsStore(Path.Combine(dataPath, "settings.json"));
        CurrentSettings = await _settingsStore.LoadAsync();
        var protector = new DpapiSecretProtector();
        var connectionString = $"Data Source={Path.Combine(dataPath, "auth.db")}";
        IOtpRepository repository = new SqliteOtpRepository(connectionString, protector);
        var generator = new OtpGenerator();
        var clipboard = new ClipboardService();
        _viewModel = new MainViewModel(repository, generator, clipboard);
        _quickViewModel = new MainViewModel(repository, generator, clipboard);

        var crypto = new LibsodiumEnteCryptoCodec();
        _sessionStore = new DpapiEnteSessionStore(Path.Combine(dataPath, "ente-session.bin"), protector);
        _authenticatorKeyStore = new DpapiAuthenticatorKeyStore(Path.Combine(dataPath, "authenticator-key.bin"), protector);
        var accountHttp = new HttpClient { BaseAddress = new Uri("https://api.ente.io/") };
        accountHttp.DefaultRequestHeaders.Add("X-Client-Package", "io.ente.auth.windows");
        accountHttp.DefaultRequestHeaders.Add("X-Client-Version", "1.0.0");
        _authenticationService = new EnteAuthenticationService(new EnteAccountClient(accountHttp), crypto);
        var authenticatorHttp = new HttpClient { BaseAddress = new Uri("https://api.ente.io/") };
        authenticatorHttp.DefaultRequestHeaders.Add("X-Client-Package", "io.ente.auth.windows");
        authenticatorHttp.DefaultRequestHeaders.Add("X-Client-Version", "1.0.0");
        var authenticatorClient = new EnteAuthenticatorClient(authenticatorHttp, () => _session?.AuthToken);
        _authenticatorKeyManager = new EnteAuthenticatorKeyManager(authenticatorClient, crypto, _authenticatorKeyStore);
        _syncStateStore = new SqliteAuthenticatorSyncStateStore(connectionString, protector);
        _syncService = new EnteAuthenticatorSyncService(authenticatorClient,
            _syncStateStore, new EnteAuthenticatorEntityCodec(crypto));
        try { _session = await _sessionStore.LoadAsync(); }
        catch
        {
            _session = null;
            LastSyncError = "A damaged saved session was removed. Sign in again to reconnect Ente.";
            try { await _sessionStore.ClearAsync(); } catch { }
        }

        _isLocked = CurrentSettings.AppLockEnabled;
        InitializeTrayIcon();
        Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged += async _ =>
        {
            if (CurrentSettings.AutoSyncOnNetwork && _session is not null)
            {
                var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
                if (profile is not null && profile.GetNetworkConnectivityLevel() == Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess)
                {
                    try { await SyncNowAsync(); } catch { }
                }
            }
        };
        if (_session is not null) _ = SyncAfterStartupAsync();
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var startupActivation = Environment.GetCommandLineArgs().Contains("--autostart") || activation.Kind == ExtendedActivationKind.StartupTask;
        var disposition = LaunchPolicy.Resolve(startupActivation, CurrentSettings.LaunchMode);
        if (disposition is LaunchDisposition.ShowWindow or LaunchDisposition.ShowMinimized)
        {
            if (disposition == LaunchDisposition.ShowMinimized) await ShowMainWindowMinimizedAsync();
            else await ShowMainWindowAsync();
        }
    }

    private static void InitializeTrayIcon()
    {
        var app = (App)Current;
        var quickPanel = (XamlUICommand)app.Resources["OpenQuickPanelCommand"];
        quickPanel.ExecuteRequested += async (_, _) => await ShowQuickPanelAsync();
        var open = (XamlUICommand)app.Resources["OpenWindowCommand"];
        open.ExecuteRequested += async (_, _) => await ShowMainWindowAsync();
        var lockCommand = (XamlUICommand)app.Resources["LockCommand"];
        lockCommand.ExecuteRequested += (_, _) => Lock();
        var exit = (XamlUICommand)app.Resources["ExitCommand"];
        exit.ExecuteRequested += (_, _) => Quit();
        _trayIcon = (TaskbarIcon)app.Resources["TrayIcon"];
        _trayIcon.ForceCreate();
    }



    public static async Task ShowMainWindowAsync()
    {
        if (_viewModel is null) return;
        if (_isLocked && !await VerifyPresenceAsync("Unlock Ente Auth Community"))
        {
            ShowPresenceFailure();
            return;
        }
        _isLocked = false;
        await _viewModel.ReloadAsync();
        _mainWindow ??= new MainWindow(_viewModel, new EnteEncryptedBackupCodec(new LibsodiumEnteCryptoCodec()));
        if (_mainWindow.AppWindow.Presenter is OverlappedPresenter
            {
                State: OverlappedPresenterState.Minimized,
            } presenter)
        {
            presenter.Restore(activateWindow: true);
        }
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private static async Task ShowMainWindowMinimizedAsync()
    {
        if (_viewModel is null) return;
        await _viewModel.ReloadAsync();
        _mainWindow ??= new MainWindow(_viewModel, new EnteEncryptedBackupCodec(new LibsodiumEnteCryptoCodec()));
        if (_mainWindow.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            // Set the requested state while the new window is still hidden so startup does not flash visibly.
            presenter.Minimize(activateWindow: false);
            _mainWindow.AppWindow.ShowOnceWithRequestedStartupState();
            return;
        }

        _mainWindow.Show();
    }

    public static void ShowMainWindow() => _ = ShowMainWindowAsync();

    private static async Task ShowQuickPanelAsync()
    {
        if (_quickViewModel is null) return;
        _quickPanel?.Hide();
        if (_isLocked && !await VerifyPresenceAsync("Open your authenticator codes"))
        {
            ShowPresenceFailure();
            return;
        }
        _isLocked = false;
        await _quickViewModel.ReloadAsync();
        _quickPanel ??= new QuickPanelWindow(_quickViewModel);
        await _quickPanel.PrepareAsync();
        _quickPanel.Show();
        _quickPanel.Activate();
    }

    public static void Lock()
    {
        _isLocked = true;
        _viewModel?.Lock();
        _quickViewModel?.Lock();
        _quickPanel?.Hide();
        _mainWindow?.Hide();
    }

    private static void ShowPresenceFailure()
    {
        _presenceFailureWindow ??= new PresenceFailureWindow();
        _presenceFailureWindow.ShowNearTray();
    }

    private static async Task<bool> VerifyPresenceAsync(string message)
    {
        if (!await PresenceGateLock.WaitAsync(0)) return false;
        try { return await PresenceGate.VerifyAsync(message); }
        finally { PresenceGateLock.Release(); }
    }

    public static async Task UpdateSettingsAsync(AppSettings settings)
    {
        CurrentSettings = settings;
        if (_settingsStore is not null) await _settingsStore.SaveAsync(settings);
    }

    public static async Task<bool> SetLaunchAtSignInAsync(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key is not null)
            {
                if (enabled)
                {
                    key.SetValue("EnteAuthCommunity", $"\"{Environment.ProcessPath}\" --autostart");
                }
                else
                {
                    key.DeleteValue("EnteAuthCommunity", false);
                }
            }
            await UpdateSettingsAsync(CurrentSettings with { LaunchAtSignIn = enabled });
            return enabled;
        }
        catch
        {
            await UpdateSettingsAsync(CurrentSettings with { LaunchAtSignIn = false });
            return false;
        }
    }

    public static async Task<EnteLoginResult> LoginAsync(string email, string password)
    {
        if (_authenticationService is null) throw new InvalidOperationException("Account authentication is not initialized.");
        var result = await _authenticationService.LoginAsync(email, password);
        if (result is EnteLoginResult.Authenticated authenticated) await ApplySessionAsync(authenticated.Session);
        return result;
    }

    public static async Task<EnteLoginResult> CompleteTotpAsync(string email, string sessionId, string code, string password)
    {
        if (_authenticationService is null) throw new InvalidOperationException("Account authentication is not initialized.");
        var result = await _authenticationService.CompleteTotpAsync(email, sessionId, code, password);
        if (result is EnteLoginResult.Authenticated authenticated) await ApplySessionAsync(authenticated.Session);
        return result;
    }

    public static async Task SyncNowAsync()
    {
        await SyncLifecycleGate.WaitAsync();
        try
        {
            var session = _session;
            if (session is null || _authenticatorKeyManager is null || _syncService is null) return;
            var authenticatorKey = await _authenticatorKeyManager.GetOrCreateAsync(session.MasterKey, _syncCancellation.Token);
            try
            {
                await _syncService.SyncAsync(authenticatorKey, _syncCancellation.Token);
                if (_viewModel is not null) await _viewModel.ReloadAsync();
                if (_quickViewModel is not null) await _quickViewModel.ReloadAsync();
                LastSyncError = null;
            }
            finally { CryptographicOperations.ZeroMemory(authenticatorKey); }
        }
        catch (OperationCanceledException) when (_syncCancellation.IsCancellationRequested) { throw; }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized || ex.Message.Contains("401"))
        {
            LastSyncError = "Session expired or rejected by Ente. Automatically signing out.";
            _ = SignOutAsync();
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText("sync_error_log.txt", ex.ToString());
            LastSyncError = "Synchronization failed. Retry to finish applying remote and local changes.";
            throw;
        }
        finally { SyncLifecycleGate.Release(); }
    }

    public static async Task SignOutAsync()
    {
        _syncCancellation.Cancel();
        await SyncLifecycleGate.WaitAsync();
        try
        {
            if (_sessionStore is not null) await _sessionStore.ClearAsync();
            if (_authenticatorKeyStore is not null) await _authenticatorKeyStore.ClearAsync();
            if (_session is not null)
            {
                CryptographicOperations.ZeroMemory(_session.MasterKey);
                CryptographicOperations.ZeroMemory(_session.SecretKey);
            }
            _session = null;
            LastSyncError = null;
        }
        finally
        {
            _syncCancellation.Dispose();
            _syncCancellation = new CancellationTokenSource();
            SyncLifecycleGate.Release();
        }
    }

    private static async Task ApplySessionAsync(EnteSession session)
    {
        if (_syncStateStore is not null) await _syncStateStore.BindAccountAsync(session.UserId);
        if (_sessionStore is not null) await _sessionStore.SaveAsync(session);
        _session = session;
        try { await SyncNowAsync(); }
        catch { LastSyncError = "Signed in, but synchronization failed. Retry to finish applying remote and local changes."; }
    }

    private static async Task SyncAfterStartupAsync()
    {
        try { await SyncNowAsync(); }
        catch { LastSyncError = "Automatic synchronization failed. Open Settings and try Sync now."; }
    }

    private static void Quit()
    {
        IsQuitting = true;
        _trayIcon?.Dispose();
        _quickPanel?.Close();
        _presenceFailureWindow?.Close();
        _mainWindow?.Close();
        if (_mainWindow is null) Environment.Exit(0);
    }
}
