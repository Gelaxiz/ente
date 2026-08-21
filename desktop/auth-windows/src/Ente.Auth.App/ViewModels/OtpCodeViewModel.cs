using CommunityToolkit.Mvvm.ComponentModel;
using Ente.Auth.Core.Abstractions;
using Ente.Auth.Core.Models;

namespace Ente.Auth.App.ViewModels;

public sealed class OtpCodeViewModel(OtpAccount account, IOtpGenerator generator) : ObservableObject
{
    private string _code = "------";
    private int _secondsRemaining;
    private double _progress;
    private string _countdownText = string.Empty;

    public OtpAccount Account { get; } = account;
    public string Issuer => Account.DisplayName;
    public string AccountName => Account.AccountName;
    public bool IsPinned => Account.IsPinned;
    public string Code { get => _code; private set => SetProperty(ref _code, value); }
    public int SecondsRemaining { get => _secondsRemaining; private set => SetProperty(ref _secondsRemaining, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string CountdownText { get => _countdownText; private set => SetProperty(ref _countdownText, value); }
    public string AutomationName =>
        $"Copy code {Code} for {Issuer}, {AccountName}. {(IsPinned ? "Pinned. " : string.Empty)}{CountdownText}.";

    public void Refresh(DateTimeOffset now)
    {
        var snapshot = generator.Generate(Account, now);
        Code = snapshot.Code;
        SecondsRemaining = snapshot.SecondsRemaining;
        Progress = snapshot.Progress * 100;
        CountdownText = Account.Kind == OtpKind.Hotp ? $"Counter {Account.Counter}" : $"{snapshot.SecondsRemaining}s";
        OnPropertyChanged(nameof(AutomationName));
    }
}
