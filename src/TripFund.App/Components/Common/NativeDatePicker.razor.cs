using Microsoft.AspNetCore.Components;
using TripFund.App.Services;
using Microsoft.Maui.Devices;

namespace TripFund.App.Components.Common
{
    public partial class NativeDatePicker
    {
        [Parameter] public DateTime Value { get; set; }
        [Parameter] public EventCallback<DateTime> ValueChanged { get; set; }
        [Parameter] public string? Label { get; set; }
        [Parameter] public string? Class { get; set; }

        [Inject] private INativeDatePickerService DatePickerService { get; set; } = default!;

        private bool isAndroid = DeviceInfo.Current.Platform == DevicePlatform.Android;

        private string GetFormattedDate()
        {
            var today = DateTime.Today;
            if (Value.Date == today) return "Oggi";
            if (Value.Date == today.AddDays(-1)) return "Ieri";
            return Value.ToString("dd/MM/yyyy");
        }

        private async Task HandleClick()
        {
            if (isAndroid)
            {
                var result = await DatePickerService.ShowDatePickerAsync(Value);
                if (result.HasValue)
                {
                    var newValue = new DateTime(result.Value.Year, result.Value.Month, result.Value.Day, Value.Hour, Value.Minute, Value.Second);
                    await ValueChanged.InvokeAsync(newValue);
                }
            }
        }

        private async Task OnInputChanged(ChangeEventArgs e)
        {
            if (!isAndroid && DateTime.TryParse(e.Value?.ToString(), out var date))
            {
                var newValue = new DateTime(date.Year, date.Month, date.Day, Value.Hour, Value.Minute, Value.Second);
                await ValueChanged.InvokeAsync(newValue);
            }
        }
    }
}
