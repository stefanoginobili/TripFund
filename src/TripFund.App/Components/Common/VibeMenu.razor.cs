using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace TripFund.App.Components.Common;

public partial class VibeMenu : ComponentBase, IAsyncDisposable
{
    [Parameter] public RenderFragment Trigger { get; set; } = default!;
    [Parameter] public RenderFragment ChildContent { get; set; } = default!;

    private ElementReference _triggerRef;
    private ElementReference _menuRef;
    private bool IsOpen { get; set; }

    private async Task ToggleMenu()
    {
        if (IsOpen)
        {
            await CloseMenu();
        }
        else
        {
            await OpenMenu();
        }
    }

    private async Task OpenMenu()
    {
        IsOpen = true;
        StateHasChanged();

        // Lock scroll and position menu
        await JSRuntime.InvokeVoidAsync("appLogic.lockScroll");
        
        // Wait for render to get menuRef
        await Task.Delay(10); 
        await JSRuntime.InvokeVoidAsync("appLogic.positionMenu", _triggerRef, _menuRef);
    }

    private async Task CloseMenu()
    {
        if (!IsOpen) return;
        
        IsOpen = false;
        await JSRuntime.InvokeVoidAsync("appLogic.unlockScroll");
        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (IsOpen)
        {
            try 
            {
                await JSRuntime.InvokeVoidAsync("appLogic.unlockScroll");
            }
            catch (JSDisconnectedException) { }
            catch (TaskCanceledException) { }
        }
    }
}
