using Microsoft.AspNetCore.Components;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;

namespace TripFund.App.Components.Pages
{
    public partial class Home
    {
        [Inject] private LocalStorageService Storage { get; set; } = default!;
        [Inject] private IRemoteStorageService RemoteStorage { get; set; } = default!;
        [Inject] private IAlertService Alert { get; set; } = default!;

        private List<TripListItem> currentTrips = new();
        private List<TripListItem> futureTrips = new();
        private List<TripListItem> pastTrips = new();
        private bool isLoading = true;
        private bool isSearchingRemote = false;
        private string loadingMessage = "Ricerca configurazione viaggio...";
        private string loadingSubMessage = "Stiamo scaricando i dati dal server remoto.";
        private bool showSyncSelector = false;
        private bool isJoining = false;

        protected override async Task OnInitializedAsync()
        {
            await Storage.CleanupIncompleteImportsAsync();
            await Storage.CleanupBrokenTripsAsync();
            await Storage.CleanupTempFoldersAsync();
            await LoadTrips();
        }

        private async Task NavigateToSettings() => await NavService.NavigateAsync("/", "/settings");
        private async Task NavigateToCreate(RemoteStorageSelection? selection) 
        {
            var uri = "/create-trip";
            if (selection != null)
            {
                uri += $"?provider={selection.Provider}";
                foreach (var p in selection.Parameters)
                {
                    uri += $"&{p.Key}={Uri.EscapeDataString(p.Value)}";
                }
            }
            await NavService.NavigateAsync("/", uri);
        }
        private async Task NavigateToTrip(string slug) => await NavService.NavigateAsync("/", $"/trip/{slug}");

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

        private async Task HandleSyncSelectionCompleted(RemoteStorageSelection? selection)
        {
            showSyncSelector = false;

            if (isJoining)
            {
                if (selection == null) return;
                await HandleJoinTrip(selection);
            }
            else
            {
                if (selection != null)
                {
                    loadingMessage = "Verifica cartella remota...";
                    loadingSubMessage = "Stiamo controllando che la cartella sia vuota.";
                    isSearchingRemote = true;
                    StateHasChanged();
                    try
                    {
                        var isEmpty = await RemoteStorage.IsRemoteLocationEmptyAsync(selection.Provider, selection.Parameters);
                        if (!isEmpty)
                        {
                            isSearchingRemote = false;
                            StateHasChanged();
                            await Alert.ShowAlertAsync("Errore", "La cartella remota deve esistere ed essere vuota.", type: AlertType.Error);
                            return;
                        }
                    }
                    finally
                    {
                        isSearchingRemote = false;
                        StateHasChanged();
                    }
                }
                await NavigateToCreate(selection);
            }
        }

        private async Task LoadTrips()
        {
            isLoading = true;
            var registry = await Storage.GetTripRegistryAsync();
            var allLoaded = new List<TripListItem>();

            foreach (var entry in registry.Trips)
            {
                var config = await Storage.GetLocalTripStorage(entry.Key).GetTripConfigAsync();
                if (config != null)
                {
                    allLoaded.Add(new TripListItem 
                    { 
                        Slug = entry.Key, 
                        Config = config,
                        RemoteStorage = entry.Value.RemoteStorage
                    });
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
            loadingMessage = "Ricerca configurazione viaggio...";
            loadingSubMessage = "Stiamo scaricando i dati dal server remoto.";
            isSearchingRemote = true;
            StateHasChanged();
            
            try
            {
                var metadata = await RemoteStorage.GetRemoteTripMetadataAsync(selection.Provider, selection.Parameters);
                
                isSearchingRemote = false;
                StateHasChanged();

                if (metadata == null)
                {
                    await Alert.ShowAlertAsync("Errore", "Impossibile trovare un viaggio valido nella posizione specificata.", type: AlertType.Error);
                    return;
                }

                var confirmed = await Alert.ConfirmAsync("Conferma", 
                    $"Vuoi aggiungere il viaggio creato da <b>{metadata.Author}</b> in data <b>{metadata.CreatedAt:dd/MM/yyyy}</b>?",
                    "Conferma", "Annulla");

                if (!confirmed) return;

                string remoteId = RemoteStorage.GetRemoteUniqueId(selection.Provider, selection.Parameters) ?? "";
                string slug = SlugUtility.GenerateSlug(metadata.TripSlug);

                // Safety check: fail if local folder already exists
                var tripDir = Path.Combine(Storage.TripsPath, slug);
                if (Directory.Exists(tripDir))
                {
                    await Alert.ShowAlertAsync("Errore", "Questo viaggio è già stato importato localmente.", type: AlertType.Error);
                    return;
                }

                var registry = await Storage.GetTripRegistryAsync();
                registry.Trips[slug] = new TripRegistryEntry 
                { 
                    CreatedAt = DateTime.UtcNow,
                    RemoteStorage = new RemoteStorageConfig
                    {
                        Provider = selection.Provider,
                        RemoteUniqueId = remoteId,
                        Parameters = selection.Parameters,
                        Readonly = selection.IsReadonly
                    }                };
                await Storage.SaveTripRegistryAsync(registry);

                await Storage.InitializeInitialImportAsync(slug);

                loadingMessage = "Sincronizzazione in corso...";
                loadingSubMessage = "Stiamo scaricando i dati completi del viaggio.";
                isSearchingRemote = true;
                StateHasChanged();

                try
                {
                    await RemoteStorage.SynchronizeAsync(slug);
                    Storage.CompleteInitialImport(slug);
                    await NavigateToTrip(slug);
                }
                catch (SyncConflictException)
                {
                    await Storage.DeleteTripAsync(slug);
                    await Alert.ShowAlertAsync("Errore", "Il viaggio remoto contiene dei conflitti. Risolvili su un altro dispositivo prima di importarlo.", type: AlertType.Error);
                }
                catch (Exception)
                {
                    await Storage.DeleteTripAsync(slug);
                    await Alert.ShowAlertAsync("Errore", "Sincronizzazione fallita. Assicurati di avere una connessione attiva.", type: AlertType.Error);
                }
            }
            finally
            {
                isSearchingRemote = false;
                StateHasChanged();
            }
        }

        private string FormatDates(DateTime start, DateTime end)
        {
            if (start.Year == end.Year && start.Month == end.Month && start.Day == end.Day)
            {
                return $"{start:d MMMM yyyy}";
            }
            if (start.Year == end.Year && start.Month == end.Month)
            {
                return $"{start:%d} - {end:d MMMM yyyy}";
            }
            if (start.Year == end.Year)
            {
                return $"{start:d MMMM} - {end:d MMMM yyyy}";
            }
            return $"{start:d MMMM yyyy} - {end:d MMMM yyyy}";
        }

        private double CalculateProgress(DateTime start, DateTime end)
        {
            var now = DateTime.Now;
            var startDate = start.Date; // 00:00:00
            var endDate = end.Date.AddDays(1).AddSeconds(-1); // 23:59:59

            if (now < startDate) return 0;
            if (now > endDate) return 1;

            var totalDuration = (endDate - startDate).TotalSeconds;
            var elapsedDuration = (now - startDate).TotalSeconds;

            return Math.Clamp(elapsedDuration / totalDuration, 0, 1);
        }

        private class TripListItem
        {
            public string Slug { get; set; } = "";
            public TripConfig Config { get; set; } = new();
            public RemoteStorageConfig? RemoteStorage { get; set; }
        }
    }
}
