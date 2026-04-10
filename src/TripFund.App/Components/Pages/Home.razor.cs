using Microsoft.AspNetCore.Components;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;

namespace TripFund.App.Components.Pages
{
    public partial class Home
    {
        [Inject] private LocalTripStorageService Storage { get; set; } = default!;
        [Inject] private IRemoteStorageService RemoteStorage { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;
        [Inject] private IAlertService Alert { get; set; } = default!;

        private List<TripListItem> currentTrips = new();
        private List<TripListItem> futureTrips = new();
        private List<TripListItem> pastTrips = new();
        private bool isLoading = true;
        private bool showSyncSelector = false;
        private bool isJoining = false;

        protected override async Task OnInitializedAsync()
        {
            await LoadTrips();
        }

        private void NavigateToSettings() => Nav.NavigateTo("/settings");
        private void NavigateToCreate(RemoteStorageSelection selection) 
        {
            var uri = $"/create-trip?provider={selection.Provider}";
            foreach (var p in selection.Parameters)
            {
                uri += $"&{p.Key}={Uri.EscapeDataString(p.Value)}";
            }
            Nav.NavigateTo(uri);
        }
        private void NavigateToTrip(string slug) => Nav.NavigateTo($"/trip/{slug}");

        private void HandleCreateTripClick()
        {
            isJoining = false;
            showSyncSelector = true;
        }

        private void HandleJoinTripClick()
        {
            isJoining = true;
            showSyncSelector = true;
        }

        private async Task HandleSyncSelectionCompleted(RemoteStorageSelection selection)
        {
            showSyncSelector = false;

            if (isJoining)
            {
                await HandleJoinTrip(selection);
            }
            else
            {
                var isEmpty = await RemoteStorage.IsRemoteLocationEmptyAsync(selection.Provider, selection.Parameters);
                if (!isEmpty)
                {
                    await Alert.ShowAlertAsync("Errore", "La posizione remota deve esistere ed essere vuota.");
                    return;
                }
                NavigateToCreate(selection);
            }
        }

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

        private async Task HandleJoinTrip(RemoteStorageSelection selection)
        {
            var remoteConfig = await RemoteStorage.GetRemoteTripConfigAsync(selection.Provider, selection.Parameters);
            
            if (remoteConfig == null)
            {
                await Alert.ShowAlertAsync("Errore", "Impossibile trovare i dati del viaggio nella posizione specificata.");
                return;
            }

            var confirmed = await Alert.ConfirmAsync("Conferma", 
                $"Vuoi aggiungere il viaggio {remoteConfig.Name} dal {remoteConfig.StartDate:dd/MM/yyyy} al {remoteConfig.EndDate:dd/MM/yyyy}?",
                "Conferma", "Annulla");

            if (!confirmed) return;

            string slug = SlugUtility.GenerateSlug(remoteConfig.Name);
            var existingConfig = await Storage.GetTripConfigAsync(slug);
            
            if (existingConfig == null)
            {
                var settings = await Storage.GetAppSettingsAsync();
                var deviceId = settings?.DeviceId ?? "unknown";
                await Storage.SaveTripConfigAsync(slug, remoteConfig, deviceId);
            }

            var registry = await Storage.GetTripRegistryAsync();
            registry.Trips[slug] = new TripRegistryEntry 
            { 
                CreatedAt = DateTime.UtcNow,
                RemoteStorage = new RemoteStorageConfig 
                { 
                    Provider = selection.Provider, 
                    Parameters = selection.Parameters
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
