using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;

namespace TripFund.App.Components.Pages
{
    public partial class Settings
    {
        [Inject] private LocalTripStorageService Storage { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        private string authorName = string.Empty;
        private string? deviceId = null;

        private string DisplayDeviceId => deviceId ?? (string.IsNullOrEmpty(authorName) ? "mario-rossi-abcd1234" : $"{SlugUtility.GenerateSlug(authorName).Trim('-')}-xxxxxxxx".Trim('-'));

        protected override async Task OnInitializedAsync()
        {
            var settings = await Storage.GetAppSettingsAsync();
            if (settings != null)
            {
                authorName = settings.AuthorName;
                deviceId = settings.DeviceId;
            }
        }

        private async Task SaveSettings()
        {
            if (string.IsNullOrWhiteSpace(authorName)) return;

            // If for some reason it's missing (should not happen after onboarding),
            // we generate a fallback but we never overwrite an existing one.
            var finalDeviceId = deviceId ?? $"{SlugUtility.GenerateSlug(authorName).Trim('-')}-{Guid.NewGuid().ToString("n").Substring(0, 8)}".Trim('-');

            var settings = new AppSettings
            {
                AuthorName = authorName,
                DeviceId = finalDeviceId
            };

            await Storage.SaveAppSettingsAsync(settings);
            deviceId = finalDeviceId;
            Nav.NavigateTo("/");
        }
    }
}
