namespace TripFund.App.Services;

public enum AlertType
{
    Information,
    Warning,
    Error
}

public interface IAlertService
{
    Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel", AlertType type = AlertType.Information);
    Task ShowAlertAsync(string title, string message, string cancel = "OK", AlertType type = AlertType.Information);
}
