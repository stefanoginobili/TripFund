using Microsoft.AspNetCore.Components;
using TripFund.App.Services;
using Microsoft.Maui.Devices;

namespace TripFund.App.Components.Common
{
    public partial class NativeTimePicker
    {
        [Parameter] public DateTime Value { get; set; }
        [Parameter] public EventCallback<DateTime> ValueChanged { get; set; }
        [Parameter] public string? Label { get; set; }
        [Parameter] public string? Class { get; set; }

        [Inject] private INativeDatePickerService DatePickerService { get; set; } = default!;

        private bool isAndroid = DeviceInfo.Current.Platform == DevicePlatform.Android;

        private async Task HandleClick()
        {
            if (isAndroid)
            {
                var result = await DatePickerService.ShowTimePickerAsync(Value);
                if (result.HasValue)
                {
                    var newValue = Value.Date.Add(result.Value);
                    await ValueChanged.InvokeAsync(newValue);
                }
            }
        }

        private async Task OnInputChanged(ChangeEventArgs e)
        {
            if (!isAndroid && TimeSpan.TryParse(e.Value?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out var time))
            {
                var newValue = Value.Date.Add(time);
                await ValueChanged.InvokeAsync(newValue);
            }
        }
    }
}
