using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripFund.App.Models;
using TripFund.App.Services;

namespace TripFund.App.Components.Pages
{
    public partial class CreateTrip
    {
        [Inject] private LocalTripStorageService Storage { get; set; } = default!;
        [Inject] private IDriveService Drive { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        private string tripName = "";
        private string tripSlug = "";
        private string description = "";
        private DateTime startDate = DateTime.Today;
        private DateTime endDate = DateTime.Today.AddDays(7);
        private string error = "";

        private Dictionary<string, Currency> currencies = new();

        private void TrimName()
        {
            tripName = tripName?.Trim() ?? "";
        }

        private void TrimDescription()
        {
            description = description?.Trim() ?? "";
        }

        private async Task HandleCreate()
        {
            tripName = tripName?.Trim() ?? "";
            tripSlug = tripSlug?.Trim() ?? "";
            description = description?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(tripName)) { error = "Il nome è obbligatorio."; return; }
            if (string.IsNullOrWhiteSpace(tripSlug)) { error = "Lo slug è obbligatorio."; return; }
            if (currencies.Count == 0) { error = "Aggiungi almeno una valuta."; return; }

            var registry = await Storage.GetTripRegistryAsync();
            if (registry.Trips.ContainsKey(tripSlug))
            {
                error = "Esiste già un viaggio con questo slug locale.";
                return;
            }

            var folder = await Drive.PickFolderAsync();
            if (folder == null) return;

            var tripConfig = new TripConfig
            {
                Id = Guid.NewGuid().ToString(),
                Name = tripName,
                Description = description,
                StartDate = startDate,
                EndDate = endDate,
                CreatedAt = DateTime.UtcNow,
                Currencies = currencies
            };

            var settings = await Storage.GetAppSettingsAsync();

            await Storage.SaveTripConfigAsync(tripSlug, tripConfig, settings?.DeviceId ?? "unknown");

            registry.Trips[tripSlug] = new TripRegistryEntry 
            { 
                CreatedAt = DateTime.UtcNow,
                Sync = new SyncConfig 
                { 
                    Provider = "google-drive", 
                    Parameters = new Dictionary<string, string> { { "folderId", folder.Id } } 
                } 
            };
            await Storage.SaveTripRegistryAsync(registry);

            Nav.NavigateTo($"/trip/{tripSlug}");
        }
    }
}
