using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using TripFund.App.Utilities;

namespace TripFund.App.Components.Layout
{
    public partial class MainLayout : LayoutComponentBase, IDisposable
    {
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                await JSRuntime.InvokeVoidAsync("appLogic.init");
            }
        }

        protected override void OnInitialized()
        {
            Nav.LocationChanged += HandleLocationChanged;
        }

        private async void HandleLocationChanged(object? sender, LocationChangedEventArgs e)
        {
            try 
            {
                await JSRuntime.InvokeVoidAsync("appLogic.resetScroll");
            }
            catch (Exception ex)
            {
                // Silently fail if JS runtime is not ready yet during navigation
                TripFundLogger.Error("Scroll reset error", ex);
            }
        }

        public void Dispose()
        {
            Nav.LocationChanged -= HandleLocationChanged;
        }
    }
}
