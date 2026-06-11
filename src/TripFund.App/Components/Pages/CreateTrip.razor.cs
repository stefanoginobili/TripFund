using Microsoft.AspNetCore.Components;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;
using TripFund.App.Constants;

namespace TripFund.App.Components.Pages
{
    public partial class CreateTrip
    {
        [Inject] private LocalStorageService Storage { get; set; } = default!;
        [Inject] private IRemoteStorageService RemoteStorage { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;
        [Inject] private IAlertService Alerts { get; set; } = default!;

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
        private string? originalConfigJson;
        private bool isInternalNavigationAllowed = false;

        private Dictionary<string, Currency> currencies = new();
        private Dictionary<string, ExpenseCategory> categories = new();

        protected override void OnInitialized()
        {
            NavService.SetBeforeNavigateAction(ConfirmDiscardChanges);
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
                var suggestedName = RemoteStorage.GetSuggestedTripName(Provider, remoteStorageParameters);
                if (!string.IsNullOrEmpty(suggestedName))
                {
                    tripName = suggestedName;
                    tripSlug = SlugUtility.GenerateSlug(tripName);
                }
            }

            originalConfigJson = System.Text.Json.JsonSerializer.Serialize(BuildTripConfig());
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

            var tripConfig = BuildTripConfig();
            tripConfig.Id = finalSlug;

            var settings = await Storage.GetAppSettingsAsync();

            await Storage.GetLocalTripStorage(finalSlug).SaveTripConfigAsync(tripConfig, settings?.DeviceId ?? "unknown");

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

            isInternalNavigationAllowed = true;
            await NavService.NavigateAsync("/", $"/trip/{finalSlug}");
        }

        private TripConfig BuildTripConfig()
        {
            return new TripConfig
            {
                Id = tripSlug,
                Name = tripName?.Trim() ?? "",
                Description = description?.Trim() ?? "",
                StartDate = startDate,
                EndDate = endDate,
                CreatedAt = DateTime.UtcNow,
                Currencies = currencies,
                Categories = new TripCategories { Expenses = categories }
            };
        }

        private bool HasChanges()
        {
            if (originalConfigJson == null) return false;

            var current = BuildTripConfig();
            var original = System.Text.Json.JsonSerializer.Deserialize<TripConfig>(originalConfigJson);
            if (original == null) return true;

            if (current.Name != original.Name) return true;
            if (current.Description != original.Description) return true;
            if (current.StartDate != original.StartDate) return true;
            if (current.EndDate != original.EndDate) return true;
            if (current.Currencies.Count != original.Currencies.Count) return true;
            // Simplified currency check
            foreach (var k in current.Currencies.Keys)
            {
                if (!original.Currencies.ContainsKey(k)) return true;
            }

            return false;
        }

        private async Task HandleBeforeInternalNavigation(Microsoft.AspNetCore.Components.Routing.LocationChangingContext context)
        {
            if (isInternalNavigationAllowed) return;

            if (!await ConfirmDiscardChanges())
            {
                context.PreventNavigation();
            }
        }

        private async Task<bool> ConfirmDiscardChanges()
        {
            if (isInternalNavigationAllowed || !HasChanges()) return true;

            var confirmed = await Alerts.ConfirmAsync(
                "Modifiche non salvate",
                "Hai apportato delle modifiche. Vuoi uscire senza salvare?",
                "Esci",
                "Rimani",
                AlertType.Warning);

            if (confirmed)
            {
                isInternalNavigationAllowed = true;
            }

            return confirmed;
        }
    }
}
