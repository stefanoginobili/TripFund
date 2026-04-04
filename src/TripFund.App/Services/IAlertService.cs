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
        var page = Application.Current?.Windows[0]?.Page;
        if (page == null) return true;
#if NET10_0_OR_GREATER
        return await page.DisplayAlertAsync(title, message, accept, cancel);
#else
        return await page.DisplayAlert(title, message, accept, cancel);
#endif
    }

    public async Task ShowAlertAsync(string title, string message, string cancel = "OK")
    {
        var page = Application.Current?.Windows[0]?.Page;
        if (page == null) return;
#if NET10_0_OR_GREATER
        await page.DisplayAlertAsync(title, message, cancel);
#else
        await page.DisplayAlert(title, message, cancel);
#endif
    }
}
