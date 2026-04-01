namespace TripFund.App.Services;

public interface IAlertService
{
    Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel");
    Task ShowAlertAsync(string title, string message, string cancel = "OK");
}

public class MauiAlertService : IAlertService
{
    public async Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel")
    {
        if (Application.Current?.MainPage == null) return true;
        return await Application.Current.MainPage.DisplayAlert(title, message, accept, cancel);
    }

    public async Task ShowAlertAsync(string title, string message, string cancel = "OK")
    {
        if (Application.Current?.MainPage == null) return;
        await Application.Current.MainPage.DisplayAlert(title, message, cancel);
    }
}
