using Microsoft.AspNetCore.Components;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;

namespace TripFund.App.Components.Pages
{
    public partial class Home
    {
        [Inject] private LocalTripStorageService Storage { get; set; } = default!;
        [Inject] private IDriveService Drive { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;

        private List<TripListItem> currentTrips = new();
        private List<TripListItem> futureTrips = new();
        private List<TripListItem> pastTrips = new();
        private bool isLoading = true;

        protected override async Task OnInitializedAsync()
        {
            await LoadTrips();
        }

        private void NavigateToSettings() => Nav.NavigateTo("/settings");
        private void NavigateToCreate() => Nav.NavigateTo("/create-trip");
        private void NavigateToTrip(string slug) => Nav.NavigateTo($"/trip/{slug}");

        private async Task LoadTrips()
        {
            isLoading = true;
            var registry = await Storage.GetTripRegistryAsync();
            var allLoaded = new List<TripListItem>();

            foreach (var entry in registry.Trips)
            {
                var config = await Storage.GetTripConfigAsync(entry.Key);
                if (config != null)
                {
                    allLoaded.Add(new TripListItem { Slug = entry.Key, Config = config });
                }
            }

            var today = DateTime.Today;

            currentTrips = allLoaded
                .Where(t => today >= t.Config.StartDate.Date && today <= t.Config.EndDate.Date)
                .OrderBy(t => t.Config.StartDate)
                .ToList();

            futureTrips = allLoaded
                .Where(t => t.Config.StartDate.Date > today)
                .OrderBy(t => t.Config.StartDate)
                .ToList();

            pastTrips = allLoaded
                .Where(t => t.Config.EndDate.Date < today)
                .OrderByDescending(t => t.Config.StartDate)
                .ToList();

            isLoading = false;
        }

        private async Task HandleJoinTrip()
        {
            var folder = await Drive.PickFolderAsync();
            if (folder == null) return;

            string slug = SlugUtility.GenerateSlug(folder.Name);
            var existingConfig = await Storage.GetTripConfigAsync(slug);
            
            if (existingConfig == null)
            {
                existingConfig = new TripConfig
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = folder.Name,
                    StartDate = DateTime.Today,
                    EndDate = DateTime.Today.AddDays(7),
                    CreatedAt = DateTime.UtcNow,
                    Currencies = new Dictionary<string, Currency> { 
                        { "EUR", new Currency { Symbol = "€", Name = "Euro", ExpectedQuotaPerMember = 500 } } 
                    }
                };
                var settings = await Storage.GetAppSettingsAsync();
                var deviceId = settings?.DeviceId ?? "unknown";
                await Storage.SaveTripConfigAsync(slug, existingConfig, deviceId);
            }

            var registry = await Storage.GetTripRegistryAsync();
            registry.Trips[slug] = new TripRegistryEntry 
            { 
                CreatedAt = DateTime.UtcNow,
                Sync = new SyncConfig 
                { 
                    Provider = "google-drive", 
                    Parameters = new Dictionary<string, string> { { "folderId", folder.Id } } 
                } 
            };
            await Storage.SaveTripRegistryAsync(registry);

            NavigateToTrip(slug);
        }

        private string FormatDates(DateTime start, DateTime end)
        {
            if (start.Year == end.Year)
            {
                return $"{start:d MMM} - {end:d MMM yyyy}";
            }
            return $"{start:d MMM yyyy} - {end:d MMM yyyy}";
        }

        private class TripListItem
        {
            public string Slug { get; set; } = "";
            public TripConfig Config { get; set; } = new();
        }
    }
}
