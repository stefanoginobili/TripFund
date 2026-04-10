using Microsoft.AspNetCore.Components;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;

namespace TripFund.App.Components.Pages
{
    public partial class Onboarding
    {
        [Inject] private LocalTripStorageService Storage { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;

        private string authorName = string.Empty;
        private string deviceIdSuffix = Guid.NewGuid().ToString("n").Substring(0, 8);
        private string DeviceId => $"{SlugUtility.GenerateSlug(authorName).Trim('-')}-{deviceIdSuffix}".Trim('-');

        private void TrimAuthorName()
        {
            authorName = authorName?.Trim() ?? "";
        }

        private async Task SaveSettings()
        {
            authorName = authorName?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(authorName)) return;

            var settings = new AppSettings
            {
                AuthorName = authorName,
                DeviceId = DeviceId
            };

            await Storage.SaveAppSettingsAsync(settings);
            Nav.NavigateTo("/");
        }
    }
}
