namespace TripFund.App.Services;

public enum ToastType
{
    Success,
    Error,
    Info
}

public interface IToastService
{
    event Action<string, ToastType, int>? OnShow;
    void ShowSuccess(string message, int durationMs = 5000);
    void ShowError(string message, int durationMs = 5000);
    void ShowInfo(string message, int durationMs = 5000);
}
