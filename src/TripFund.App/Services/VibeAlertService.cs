namespace TripFund.App.Services;

public class VibeAlertService : IAlertService
{
    public event Action? OnChange;
    
    public bool IsVisible { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public string AcceptText { get; private set; } = "OK";
    public string CancelText { get; private set; } = "Annulla";
    public bool IsConfirm { get; private set; }
    public AlertType Type { get; private set; }
    public string MessageAlignment { get; private set; } = "center";
    
    private TaskCompletionSource<bool>? _tcs;

    public Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Annulla", AlertType type = AlertType.Information, string messageAlignment = "center")
    {
        _tcs?.TrySetResult(false);
        _tcs = new TaskCompletionSource<bool>();
        
        Title = title;
        Message = message;
        AcceptText = accept;
        CancelText = cancel;
        IsConfirm = true;
        IsVisible = true;
        Type = type;
        MessageAlignment = messageAlignment;
        
        NotifyStateChanged();
        
        return _tcs.Task;
    }

    public Task ShowAlertAsync(string title, string message, string cancel = "OK", AlertType type = AlertType.Information, string messageAlignment = "center")
    {
        _tcs?.TrySetResult(false);
        _tcs = new TaskCompletionSource<bool>();
        
        Title = title;
        Message = message;
        AcceptText = cancel; // For simple alert, we use accept button as the only button
        CancelText = string.Empty;
        IsConfirm = false;
        IsVisible = true;
        Type = type;
        MessageAlignment = messageAlignment;
        
        NotifyStateChanged();
        
        return _tcs.Task;
    }

    public void Close(bool result)
    {
        IsVisible = false;
        NotifyStateChanged();
        _tcs?.TrySetResult(result);
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
