using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

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
                await JSRuntime.InvokeVoidAsync("headerLogic.init");
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
                await JSRuntime.InvokeVoidAsync("headerLogic.resetScroll");
            }
            catch (Exception ex)
            {
                // Silently fail if JS runtime is not ready yet during navigation
                System.Diagnostics.Debug.WriteLine($"Scroll reset error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Nav.LocationChanged -= HandleLocationChanged;
        }
    }
}
