using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ente.Auth.App.Services;
using Ente.Auth.Core.Abstractions;
using Ente.Auth.Core.Models;
using Ente.Auth.Core.Otp;

namespace Ente.Auth.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IOtpRepository _repository;
    private readonly IOtpGenerator _generator;
    private readonly ClipboardService _clipboard;
    private string _searchText = string.Empty;
    private bool _isEmpty;
    private string _statusMessage = string.Empty;
    private IReadOnlyList<OtpAccount> _accounts = [];

    public MainViewModel(IOtpRepository repository, IOtpGenerator generator, ClipboardService clipboard)
    {
        _repository = repository;
        _generator = generator;
        _clipboard = clipboard;
        CopyCommand = new AsyncRelayCommand<OtpCodeViewModel>(CopyAsync);
    }

    public ObservableCollection<OtpCodeViewModel> Codes { get; } = [];
    public IAsyncRelayCommand<OtpCodeViewModel> CopyCommand { get; }
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
    }
    public bool IsEmpty { get => _isEmpty; private set => SetProperty(ref _isEmpty, value); }
    public int TotalCount => _accounts.Count;
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public async Task ReloadAsync()
    {
        _accounts = await _repository.GetAllAsync();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var filtered = _accounts.Where(account => query.Length == 0 ||
            account.Issuer.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            account.AccountName.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        Codes.Clear();
        foreach (var account in filtered)
        {
            var code = new OtpCodeViewModel(account, _generator);
            code.Refresh(DateTimeOffset.Now);
            Codes.Add(code);
        }
        IsEmpty = Codes.Count == 0;
    }

    public void Tick()
    {
        var now = DateTimeOffset.Now;
        foreach (var code in Codes) code.Refresh(now);
    }

    public void Lock()
    {
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        Codes.Clear();
        IsEmpty = true;
    }

    public async Task AddFromUriAsync(string uri)
    {
        var account = OtpAuthUriParser.Parse(uri);
        await _repository.UpsertAsync(account);
        await ReloadAsync();
    }

    public async Task UpsertAsync(OtpAccount account)
    {
        await _repository.UpsertAsync(account);
        await ReloadAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        await _repository.DeleteAsync(id);
        await ReloadAsync();
    }

    public async Task<int> ImportAsync(string text)
    {
        var accounts = OtpTransferCodec.Import(text);
        foreach (var account in accounts) await _repository.UpsertAsync(account);
        await ReloadAsync();
        return accounts.Count;
    }

    public string Export() => OtpTransferCodec.Export(_accounts);

    public string ExportForEncryptedBackup() =>
        string.Join('\n', _accounts.Select(OtpTransferCodec.ExportUri));

    private async Task CopyAsync(OtpCodeViewModel? code)
    {
        if (code is null) return;
        try
        {
            await _clipboard.CopyAsync(code.Code, App.CurrentSettings.ClipboardClearSeconds);
            StatusMessage = "Copied. The clipboard will clear automatically.";
            var updated = code.Account with
            {
                Counter = code.Account.Kind == OtpKind.Hotp ? code.Account.Counter + 1 : code.Account.Counter,
                LastUsedAt = DateTimeOffset.UtcNow,
            };
            await _repository.UpsertAsync(updated);
            await ReloadAsync();
        }
        catch
        {
            StatusMessage = "Could not copy the code. Try again.";
        }
    }
}
