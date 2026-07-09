namespace TripFund.App.Services;

public class VibeToastService : IToastService
{
    public event Action<string, ToastType, int>? OnShow;

    public void ShowSuccess(string message, int durationMs = 5000)
    {
        OnShow?.Invoke(message, ToastType.Success, durationMs);
    }

    public void ShowError(string message, int durationMs = 5000)
    {
        OnShow?.Invoke(message, ToastType.Error, durationMs);
    }

    public void ShowInfo(string message, int durationMs = 5000)
    {
        OnShow?.Invoke(message, ToastType.Info, durationMs);
    }
}
