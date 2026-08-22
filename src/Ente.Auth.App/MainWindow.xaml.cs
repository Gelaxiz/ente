using System.Security.Cryptography;
using System.Net;
using Ente.Auth.App.ViewModels;
using Ente.Auth.Core.Models;
using Ente.Auth.Core.Otp;
using Ente.Auth.Core.Settings;
using Ente.Auth.Core.Auth;
using Ente.Auth.Infrastructure.Backup;
using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Ente.Auth.App;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly EnteEncryptedBackupCodec _backupCodec;
    private bool _loadingSettings = true;
    private bool _backupOperationInProgress;

    public MainWindow(MainViewModel viewModel, EnteEncryptedBackupCodec backupCodec)
    {
        InitializeComponent();
        Title = "Ente Auth Community";
        Root.DataContext = viewModel;
        _backupCodec = backupCodec;
        AppWindow.SetIcon("Assets/EnteAuth.ico");
        AppWindow.Resize(WindowSizing.ToPixels(this, 820, 780));
        AppWindow.Closing += (_, args) =>
        {
            if (App.IsQuitting) return;
            args.Cancel = true;
            this.Hide();
        };
        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated && App.CurrentSettings.FocusSearchOnOpen)
                SearchBox.Focus(FocusState.Programmatic);
        };
        _timer.Tick += (_, _) =>
        {
            viewModel.Tick();
            UpdateAccountStatus();
        };
        _timer.Start();
        LoadSettings();
        UpdateAccountStatus();
    }

    private MainViewModel ViewModel => (MainViewModel)Root.DataContext;

    private void LoadSettings()
    {
        LaunchModePicker.SelectedIndex = (int)App.CurrentSettings.LaunchMode;
        LaunchAtSignInToggle.IsOn = App.CurrentSettings.LaunchAtSignIn;
        AppLockToggle.IsOn = App.CurrentSettings.AppLockEnabled;
        FocusSearchOnOpenToggle.IsOn = App.CurrentSettings.FocusSearchOnOpen;
        AutoSyncOnNetworkToggle.IsOn = App.CurrentSettings.AutoSyncOnNetwork;
        GridViewLayoutToggle.IsOn = App.CurrentSettings.GridViewLayout;
        _loadingSettings = false;
    }

    private async void AppLockToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        await App.UpdateSettingsAsync(App.CurrentSettings with { AppLockEnabled = AppLockToggle.IsOn });
    }

    private async void FocusSearchOnOpenToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        await App.UpdateSettingsAsync(App.CurrentSettings with { FocusSearchOnOpen = FocusSearchOnOpenToggle.IsOn });
    }

    private async void AutoSyncOnNetworkToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        await App.UpdateSettingsAsync(App.CurrentSettings with { AutoSyncOnNetwork = AutoSyncOnNetworkToggle.IsOn });
    }

    private async void GridViewLayoutToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        await App.UpdateSettingsAsync(App.CurrentSettings with { GridViewLayout = GridViewLayoutToggle.IsOn });
        ViewModel.NotifySettingsChanged();
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag?.ToString();
        var settings = tag == "settings";
        SettingsSurface.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        CodesSurface.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (ViewModel.Codes.FirstOrDefault() is { } first)
        {
            await ViewModel.CopyCommand.ExecuteAsync(first);
            if (ViewModel.StatusMessage.StartsWith("Copied", StringComparison.Ordinal)) this.Hide();
        }
    }

    private async void AddCode_Click(object sender, RoutedEventArgs e)
    {
        var input = new TextBox { PlaceholderText = "otpauth://totp/...", MinWidth = 430, TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Add authenticator code",
            Content = input,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try { await ViewModel.AddFromUriAsync(input.Text); }
        catch (FormatException error)
        {
            await new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = "That link could not be added",
                Content = error.Message,
                CloseButtonText = "Close",
            }.ShowAsync();
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_backupOperationInProgress) return;
        SetBackupBusy(true, "Preparing import…");
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add(".otpauth");
            picker.FileTypeFilter.Add(".json");
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            var content = await FileIO.ReadTextAsync(file);
            if (file.FileType.Equals(".json", StringComparison.OrdinalIgnoreCase) || content.TrimStart().StartsWith('{'))
            {
                var password = await PromptPasswordAsync("Decrypt Ente Auth backup", "Password", "Decrypt");
                if (password is null) return;
                SetBackupBusy(true, "Decrypting backup…");
                content = await Task.Run(() => _backupCodec.Decrypt(content, password));
            }
            var count = await ViewModel.ImportAsync(content);
            await ShowMessageAsync("Import complete", $"Added {count} authenticator code{(count == 1 ? string.Empty : "s")}.");
        }
        catch (Exception error) when (error is FormatException or InvalidDataException or IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            await ShowMessageAsync("That file could not be imported", error.Message);
        }
        finally { SetBackupBusy(false); }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_backupOperationInProgress) return;
        SetBackupBusy(true, "Preparing export…");
        try
        {
            if (ViewModel.TotalCount == 0)
            {
                await ShowMessageAsync("Nothing to export", "Add an authenticator code first.");
                return;
            }

            var choice = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = "Export all authenticator codes",
                Content = $"This exports all {ViewModel.TotalCount} authenticator codes, regardless of the current search. Encrypted backup is compatible with Ente Auth and is recommended.",
                PrimaryButtonText = "Encrypted backup",
                SecondaryButtonText = "Portable plaintext",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };
            var choiceResult = await choice.ShowAsync();
            if (choiceResult == ContentDialogResult.None) return;

            var encrypted = choiceResult == ContentDialogResult.Primary;
            string content;
            if (encrypted)
            {
                var password = await PromptNewBackupPasswordAsync();
                if (password is null) return;
                SetBackupBusy(true, "Encrypting backup…");
                try { content = await Task.Run(() => _backupCodec.Encrypt(ViewModel.ExportForEncryptedBackup(), password)); }
                catch (Exception error) when (error is CryptographicException or ArgumentException)
                {
                    await ShowMessageAsync("The backup could not be encrypted", error.Message);
                    return;
                }
            }
            else
            {
                var warning = new ContentDialog
                {
                    XamlRoot = Root.XamlRoot,
                    Title = "Export unencrypted secrets?",
                    Content = "Anyone who can read this file can generate all of your authentication codes. Delete it after transferring your accounts.",
                    PrimaryButtonText = "Continue",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                };
                if (await warning.ShowAsync() != ContentDialogResult.Primary) return;
                content = ViewModel.Export();
            }

            var picker = new FileSavePicker
            {
                SuggestedFileName = encrypted
                    ? $"ente-auth-encrypted-{DateTime.Now:yyyy-MM-dd}"
                    : $"ente-auth-export-{DateTime.Now:yyyy-MM-dd}",
            };
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            if (encrypted) picker.FileTypeChoices.Add("Encrypted Ente Auth backup", [".json"]);
            else picker.FileTypeChoices.Add("Portable otpauth links", [".otpauth"]);
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await FileIO.WriteTextAsync(file, content);
            await ShowMessageAsync("Export complete", encrypted
                ? "The backup is encrypted. Keep both the file and its password safe."
                : "The file contains unencrypted secrets. Keep it private.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync("The export failed", error.Message);
        }
        finally { SetBackupBusy(false); }
    }

    private void MoreActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: OtpCodeViewModel code } button) return;
        var flyout = new MenuFlyout();
        var pin = new MenuFlyoutItem { Text = code.Account.IsPinned ? "Unpin" : "Pin", Icon = new FontIcon { Glyph = "\uE718" } };
        pin.Click += async (_, _) => await ViewModel.UpsertAsync(code.Account with { IsPinned = !code.Account.IsPinned });
        var edit = new MenuFlyoutItem { Text = "Edit link", Icon = new FontIcon { Glyph = "\uE70F" } };
        edit.Click += async (_, _) => await EditAsync(code.Account);
        var delete = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "\uE74D" } };
        delete.Click += async (_, _) => await ConfirmDeleteAsync(code.Account);
        flyout.Items.Add(pin);
        flyout.Items.Add(edit);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(delete);
        flyout.ShowAt(button);
    }

    private async Task EditAsync(OtpAccount account)
    {
        var uri = OtpTransferCodec.Export([account]).Split('\n').First(line => line.StartsWith("otpauth://", StringComparison.Ordinal));
        var input = new TextBox { Text = uri, MinWidth = 430, TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Edit authenticator code",
            Content = input,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var replacement = OtpAuthUriParser.Parse(input.Text) with
            {
                Id = account.Id,
                IsPinned = account.IsPinned,
                LastUsedAt = account.LastUsedAt,
                Note = account.Note,
            };
            await ViewModel.UpsertAsync(replacement);
        }
        catch (FormatException error) { await ShowMessageAsync("That link could not be saved", error.Message); }
    }

    private async Task ConfirmDeleteAsync(OtpAccount account)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = $"Delete {account.DisplayName}?",
            Content = "This removes the local authenticator secret. This action cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.DeleteAsync(account.Id);
    }

    private async Task ShowMessageAsync(string title, string message) =>
        await new ContentDialog { XamlRoot = Root.XamlRoot, Title = title, Content = message, CloseButtonText = "Close" }.ShowAsync();

    private async Task<string?> PromptPasswordAsync(string title, string header, string action)
    {
        var password = new PasswordBox { Header = header, MinWidth = 320 };
        var error = CreateValidationMessage();
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(password);
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = action,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!string.IsNullOrEmpty(password.Password)) return;
            args.Cancel = true;
            error.Text = "Enter the backup password.";
            error.Visibility = Visibility.Visible;
            password.Focus(FocusState.Programmatic);
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        return password.Password;
    }

    private async Task<string?> PromptNewBackupPasswordAsync()
    {
        var password = new PasswordBox { Header = "Backup password", MinWidth = 320 };
        var confirmation = new PasswordBox { Header = "Confirm password", MinWidth = 320 };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "Use a unique password. It cannot be recovered if you lose it.",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(password);
        content.Children.Add(confirmation);
        var error = CreateValidationMessage();
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Protect encrypted backup",
            Content = content,
            PrimaryButtonText = "Encrypt",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (password.Password.Length < 12)
            {
                args.Cancel = true;
                error.Text = "Use at least 12 characters.";
                error.Visibility = Visibility.Visible;
                password.Focus(FocusState.Programmatic);
            }
            else if (!string.Equals(password.Password, confirmation.Password, StringComparison.Ordinal))
            {
                args.Cancel = true;
                error.Text = "The passwords do not match.";
                error.Visibility = Visibility.Visible;
                confirmation.Focus(FocusState.Programmatic);
            }
            else error.Visibility = Visibility.Collapsed;
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        return password.Password;
    }

    private static TextBlock CreateValidationMessage()
    {
        var message = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Assertive);
        return message;
    }

    private void SetBackupBusy(bool busy, string message = "")
    {
        _backupOperationInProgress = busy;
        ImportButton.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy;
        BackupProgressText.Text = message;
        BackupProgressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: OtpCodeViewModel code }) await ViewModel.CopyCommand.ExecuteAsync(code);
    }

    private async void LaunchModePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || LaunchModePicker.SelectedIndex < 0) return;
        await App.UpdateSettingsAsync(App.CurrentSettings with { LaunchMode = (LaunchMode)LaunchModePicker.SelectedIndex });
    }

    private async void LaunchAtSignInToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        var requested = LaunchAtSignInToggle.IsOn;
        var enabled = await App.SetLaunchAtSignInAsync(requested);
        if (enabled != LaunchAtSignInToggle.IsOn)
        {
            _loadingSettings = true;
            LaunchAtSignInToggle.IsOn = enabled;
            _loadingSettings = false;
            if (requested)
            {
                await new ContentDialog
                {
                    XamlRoot = Root.XamlRoot,
                    Title = "Launch at sign-in was not enabled",
                    Content = "Windows declined the startup request. Check Settings > Apps > Startup, then try again.",
                    CloseButtonText = "Close",
                }.ShowAsync();
            }
        }
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        var credentials = await PromptCredentialsAsync();
        if (credentials is null) return;
        SetAccountBusy(true, "Signing in securely…");
        try
        {
            var result = await App.LoginAsync(credentials.Value.Email, credentials.Value.Password);
            if (result is EnteLoginResult.TotpRequired totp)
            {
                while (true)
                {
                    var verification = await PromptTotpAsync();
                    if (verification is null) return;
                    SetAccountBusy(true, "Verifying authentication code…");
                    try
                    {
                        result = await App.CompleteTotpAsync(credentials.Value.Email, totp.SessionId,
                            verification.Value.Code, verification.Value.Password);
                        break;
                    }
                    catch (HttpRequestException error) when (IsRejected(error.StatusCode))
                    {
                        await ShowMessageAsync("Code not accepted", "Check the six-digit code and password, then try again.");
                    }
                    catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
                    {
                        await ShowMessageAsync("Session expired", "Start sign-in again to request a new authentication session.");
                        return;
                    }
                    catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        await ShowMessageAsync("Too many attempts", "Ente temporarily limited sign-in attempts. Wait a moment, then try again.");
                        return;
                    }
                }
            }

            if (result is EnteLoginResult.PasskeyRequired)
            {
                await ShowMessageAsync("Passkey required", "This account requires a passkey. Passkey login is not available in this development build yet.");
                return;
            }
            if (result is EnteLoginResult.Authenticated)
                await ShowMessageAsync("Signed in", App.LastSyncError ?? "Your Ente Auth account is connected and synchronized.");
        }
        catch (HttpRequestException error) when (IsRejected(error.StatusCode))
        {
            await ShowMessageAsync("Sign-in failed", "The password or authentication code was not accepted.");
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            await ShowMessageAsync("Sign-in session expired", "Start sign-in again to request a new session.");
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.TooManyRequests)
        {
            await ShowMessageAsync("Too many attempts", "Ente temporarily limited sign-in attempts. Wait a moment, then try again.");
        }
        catch (Exception error) when (error is CryptographicException or InvalidDataException or ArgumentException or InvalidOperationException
            or HttpRequestException or IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync("Sign-in failed", error is HttpRequestException
                ? "Ente could not be reached. Check your connection and try again."
                : error.Message);
        }
        finally
        {
            SetAccountBusy(false);
            UpdateAccountStatus();
        }
    }

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        SetAccountBusy(true, "Synchronizing…");
        try
        {
            await App.SyncNowAsync();
            await ShowMessageAsync("Sync complete", "Your authenticator codes are up to date.");
        }
        catch (Exception)
        {
            await ShowMessageAsync("Sync stopped", App.LastSyncError
                ?? "Sync stopped before all changes completed. The local vault remains usable; retry to reconcile remaining remote and local changes.");
        }
        finally
        {
            SetAccountBusy(false);
            UpdateAccountStatus();
        }
    }

    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Sign out of Ente?",
            Content = "Cloud synchronization will stop and local codes will remain protected on this Windows account. This local vault stays bound to the same Ente account; another account will be rejected unless the local vault is reset.",
            PrimaryButtonText = "Sign out",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        SetAccountBusy(true, "Signing out…");
        try
        {
            await App.SignOutAsync();
            UpdateAccountStatus();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        {
            await ShowMessageAsync("Could not sign out", "The protected session could not be removed. You remain signed in; close other programs using the app data and try again.");
        }
        finally
        {
            SetAccountBusy(false);
            UpdateAccountStatus();
        }
    }

    private async Task<(string Email, string Password)?> PromptCredentialsAsync()
    {
        var email = new TextBox { Header = "Email", MinWidth = 340, InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.EmailSmtpAddress) } } };
        var password = new PasswordBox { Header = "Password", MinWidth = 340 };
        var consent = new CheckBox { Content = "I understand and want to connect this vault." };
        var disclosure = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void UpdateDisclosure() => disclosure.Text =
            $"Signing in will merge and upload all {ViewModel.TotalCount} local authenticator code{(ViewModel.TotalCount == 1 ? string.Empty : "s")} to {email.Text.Trim().DefaultIfBlank("this Ente account")}. This Windows vault will be permanently bound to that account; using another account requires resetting the local vault.";
        email.TextChanged += (_, _) => UpdateDisclosure();
        UpdateDisclosure();
        var error = CreateValidationMessage();
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(email);
        content.Children.Add(password);
        content.Children.Add(disclosure);
        content.Children.Add(consent);
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Sign in to Ente",
            Content = content,
            PrimaryButtonText = "Sign in",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (email.Text.Trim().Length == 0)
            {
                args.Cancel = true;
                error.Text = "Enter your Ente account email.";
                error.Visibility = Visibility.Visible;
                email.Focus(FocusState.Programmatic);
            }
            else if (password.Password.Length == 0)
            {
                args.Cancel = true;
                error.Text = "Enter your Ente account password.";
                error.Visibility = Visibility.Visible;
                password.Focus(FocusState.Programmatic);
            }
            else if (consent.IsChecked != true)
            {
                args.Cancel = true;
                error.Text = "Confirm that you want to merge this local vault with the Ente account.";
                error.Visibility = Visibility.Visible;
                consent.Focus(FocusState.Programmatic);
            }
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? (email.Text.Trim(), password.Password)
            : null;
    }

    private async Task<(string Code, string Password)?> PromptTotpAsync()
    {
        var code = new PasswordBox { Header = "Six-digit authentication code", MaxLength = 6, MinWidth = 340 };
        var password = new PasswordBox { Header = "Password again", MinWidth = 340 };
        var error = CreateValidationMessage();
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock { Text = "Enter your two-factor code and password to unlock the encrypted account keys.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(code);
        content.Children.Add(password);
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Two-factor authentication",
            Content = content,
            PrimaryButtonText = "Verify",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var normalizedCode = code.Password.Trim();
            if (normalizedCode.Length != 6 || normalizedCode.Any(character => character is < '0' or > '9'))
            {
                args.Cancel = true;
                error.Text = "Enter the six-digit numeric code from your authenticator.";
                error.Visibility = Visibility.Visible;
                code.Focus(FocusState.Programmatic);
            }
            else if (password.Password.Length == 0)
            {
                args.Cancel = true;
                error.Text = "Enter your Ente account password again.";
                error.Visibility = Visibility.Visible;
                password.Focus(FocusState.Programmatic);
            }
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? (code.Password.Trim(), password.Password)
            : null;
    }

    private void SetAccountBusy(bool busy, string message = "")
    {
        SignInButton.IsEnabled = !busy;
        SyncButton.IsEnabled = !busy;
        SignOutButton.IsEnabled = !busy;
        AccountStatusText.Text = busy ? message : AccountStatusText.Text;
    }

    private void UpdateAccountStatus()
    {
        AccountStatusText.Text = App.IsSignedIn
            ? $"Connected as {App.SignedInEmail}.{(App.LastSyncError is null ? " Codes sync with Ente." : " " + App.LastSyncError)}"
            : $"Not connected. Local codes remain available without an account.{(App.LastSyncError is null ? string.Empty : " " + App.LastSyncError)}";
        SignInButton.Visibility = App.IsSignedIn ? Visibility.Collapsed : Visibility.Visible;
        SyncButton.Visibility = App.IsSignedIn ? Visibility.Visible : Visibility.Collapsed;
        SignOutButton.Visibility = App.IsSignedIn ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool IsRejected(HttpStatusCode? statusCode) =>
        statusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}

file static class AccountTextExtensions
{
    public static string DefaultIfBlank(this string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
