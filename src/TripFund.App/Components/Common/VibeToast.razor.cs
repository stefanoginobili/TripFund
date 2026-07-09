using TripFund.App.Services;
using Microsoft.AspNetCore.Components;

namespace TripFund.App.Components.Common;

public partial class VibeToast : ComponentBase, IDisposable
{
    private string message = string.Empty;
    private ToastType type = ToastType.Info;
    private bool isVisible = false;
    private CancellationTokenSource? cts;

    protected override void OnInitialized()
    {
        ToastService.OnShow += ShowToast;
    }

    private async void ShowToast(string msg, ToastType t, int duration)
    {
        message = msg;
        type = t;
        isVisible = true;
        await InvokeAsync(StateHasChanged);

        cts?.Cancel();
        cts = new CancellationTokenSource();

        try
        {
            await Task.Delay(duration, cts.Token);
            isVisible = false;
            await InvokeAsync(StateHasChanged);
        }
        catch (TaskCanceledException)
        {
            // A new toast was shown, or component was disposed
        }
    }

    public void Dispose()
    {
        ToastService.OnShow -= ShowToast;
        cts?.Cancel();
        cts?.Dispose();
    }
}
