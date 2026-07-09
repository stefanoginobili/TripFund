using TripFund.App.Services;
using Microsoft.AspNetCore.Components;

namespace TripFund.App.Components.Common;

public partial class VibeToast : ComponentBase, IDisposable
{
    private string message = string.Empty;
    private ToastType type = ToastType.Info;
    private bool isVisible = false;
    private bool isClosing = false;
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
        isClosing = false;
        await InvokeAsync(StateHasChanged);

        cts?.Cancel();
        cts = new CancellationTokenSource();

        try
        {
            await Task.Delay(duration, cts.Token);
            isClosing = true;
            await InvokeAsync(StateHasChanged);
            await Task.Delay(500, cts.Token);
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
