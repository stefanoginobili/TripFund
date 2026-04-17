namespace TripFund.App.Services;

public interface IAlertService
{
    Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel");
    Task ShowAlertAsync(string title, string message, string cancel = "OK");
}
