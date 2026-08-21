using Windows.ApplicationModel.DataTransfer;

namespace Ente.Auth.App.Services;

public sealed class ClipboardService
{
    private CancellationTokenSource? _clearCancellation;

    public Task CopyAsync(string value, int clearAfterSeconds)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(value);
        Clipboard.SetContent(package);
        Clipboard.Flush();

        _clearCancellation?.Cancel();
        _clearCancellation?.Dispose();
        _clearCancellation = new CancellationTokenSource();
        if (clearAfterSeconds > 0)
            _ = ClearLaterAsync(value, TimeSpan.FromSeconds(clearAfterSeconds), _clearCancellation.Token);
        return Task.CompletedTask;
    }

    private static async Task ClearLaterAsync(string expectedValue, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            var current = Clipboard.GetContent();
            if (current.Contains(StandardDataFormats.Text) && await current.GetTextAsync() == expectedValue)
                Clipboard.Clear();
        }
        catch (OperationCanceledException) { }
        catch { }
    }
}
