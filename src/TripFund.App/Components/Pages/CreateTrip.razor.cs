using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;
using TripFund.App.Constants;

namespace TripFund.App.Components.Pages
{
    public partial class CreateTrip
    {
        [Inject] private LocalTripStorageService Storage { get; set; } = default!;
        [Inject] private IRemoteStorageService RemoteStorage { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        [Parameter, SupplyParameterFromQuery(Name = "provider")]
        public string? Provider { get; set; }

        private Dictionary<string, string> remoteStorageParameters = new();

        private string tripName = "";
        private string tripSlug = "";
        private string description = "";
        private string remoteUniqueId = "";
        private DateTime startDate;
        private DateTime endDate;
        private string error = "";

        private Dictionary<string, Currency> currencies = new();
        private Dictionary<string, ExpenseCategory> categories = new();

        protected override void OnInitialized()
        {
            var today = DateTime.Today;
            var targetMonth = today.AddMonths(2);
            startDate = new DateTime(targetMonth.Year, targetMonth.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
            endDate = startDate.AddDays(15);

            foreach (var def in AppConstants.Categories.DefaultTripCategories)
            {
                categories[def.Key] = new ExpenseCategory
                {
                    Name = def.Value.Name,
                    Icon = def.Value.Icon,
                    Color = def.Value.Color
                };
            }

            var uri = Nav.ToAbsoluteUri(Nav.Uri);
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);

            foreach (var kvp in query)
            {
                if (kvp.Key != "provider")
                {
                    remoteStorageParameters[kvp.Key] = kvp.Value.ToString();
                }
            }

            if (!string.IsNullOrEmpty(Provider))
            {
                remoteUniqueId = RemoteStorage.GetRemoteUniqueId(Provider, remoteStorageParameters) ?? "";
            }
        }

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

            string finalSlug = string.IsNullOrEmpty(remoteUniqueId) 
                ? tripSlug 
                : SlugUtility.GenerateSlug(tripSlug + "_" + remoteUniqueId);

            var registry = await Storage.GetTripRegistryAsync();
            if (registry.Trips.ContainsKey(finalSlug))
            {
                error = "Esiste già un viaggio con questo slug locale.";
                return;
            }

            // Safety check: fail if local folder already exists
            var tripDir = Path.Combine(Storage.TripsPath, finalSlug);
            if (Directory.Exists(tripDir))
            {
                error = "Questo viaggio è già stato importato localmente.";
                return;
            }

            var tripConfig = new TripConfig
            {
                Id = finalSlug,
                Name = tripName,
                Description = description,
                StartDate = startDate,
                EndDate = endDate,
                CreatedAt = DateTime.UtcNow,
                Currencies = currencies,
                Categories = new TripCategories { Expenses = categories }
            };

            var settings = await Storage.GetAppSettingsAsync();

            await Storage.SaveTripConfigAsync(finalSlug, tripConfig, settings?.DeviceId ?? "unknown");

            RemoteStorageConfig? remoteStorage = null;
            if (!string.IsNullOrEmpty(Provider))
            {
                remoteStorage = new RemoteStorageConfig 
                { 
                    Provider = Provider, 
                    RemoteUniqueId = remoteUniqueId,
                    Parameters = remoteStorageParameters
                };
            }

            registry.Trips[finalSlug] = new TripRegistryEntry 
            { 
                CreatedAt = DateTime.UtcNow,
                RemoteStorage = remoteStorage
            };
            await Storage.SaveTripRegistryAsync(registry);

            if (remoteStorage != null)
            {
                await RemoteStorage.InitializeRemoteLocationAsync(finalSlug, remoteStorage.Provider, remoteStorage.Parameters);
                
                // Start sync in background or wait?
                // The PRD says "Synchronization process ensures eventually consistent".
                // Let's at least start it.
                _ = RemoteStorage.SynchronizeAsync(finalSlug);
            }

            Nav.NavigateTo($"/trip/{finalSlug}");
        }
    }
}
