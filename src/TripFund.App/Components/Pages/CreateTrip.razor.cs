using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripFund.App.Models;
using TripFund.App.Services;

namespace TripFund.App.Components.Pages
{
    public partial class CreateTrip
    {
        [Inject] private LocalTripStorageService Storage { get; set; } = default!;
        [Inject] private ISyncService Sync { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        [Parameter, SupplyParameterFromQuery(Name = "provider")]
        public string? Provider { get; set; }

        [Parameter, SupplyParameterFromQuery(Name = "folderUrl")]
        public string? FolderUrl { get; set; }

        [Parameter, SupplyParameterFromQuery(Name = "repository")]
        public string? Repository { get; set; }

        [Parameter, SupplyParameterFromQuery(Name = "pat")]
        public string? Pat { get; set; }

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
            if (string.IsNullOrEmpty(Provider)) { error = "Configurazione sync mancante."; return; }

            var registry = await Storage.GetTripRegistryAsync();
            if (registry.Trips.ContainsKey(tripSlug))
            {
                error = "Esiste già un viaggio con questo slug locale.";
                return;
            }

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

            var syncParameters = new Dictionary<string, string>();
            if (Provider == "google-drive")
            {
                syncParameters["folderUrl"] = FolderUrl ?? "";
            }
            else if (Provider == "git")
            {
                syncParameters["repository"] = Repository ?? "";
                syncParameters["pat"] = Pat ?? "";
            }

            registry.Trips[tripSlug] = new TripRegistryEntry 
            { 
                CreatedAt = DateTime.UtcNow,
                Sync = new SyncConfig 
                { 
                    Provider = Provider, 
                    Parameters = syncParameters
                } 
            };
            await Storage.SaveTripRegistryAsync(registry);

            Nav.NavigateTo($"/trip/{tripSlug}");
        }
    }
}
