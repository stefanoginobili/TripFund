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
        private bool isSearchingRemote = false;
        private string loadingMessage = "Ricerca configurazione viaggio...";
        private string loadingSubMessage = "Stiamo scaricando i dati dal server remoto.";
        private bool showSyncSelector = false;
        private bool isJoining = false;

        protected override async Task OnInitializedAsync()
        {
            await Storage.CleanupIncompleteImportsAsync();
            await Storage.CleanupTempFoldersAsync();
            await LoadTrips();
        }

        private void NavigateToSettings() => Nav.NavigateTo("/settings");
        private void NavigateToCreate(RemoteStorageSelection? selection) 
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
                    loadingMessage = "Verifica posizione remota...";
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
                            await Alert.ShowAlertAsync("Errore", "La posizione remota deve esistere ed essere vuota.", type: AlertType.Error);
                            return;
                        }
                    }
                    finally
                    {
                        isSearchingRemote = false;
                        StateHasChanged();
                    }
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
                string slug = SlugUtility.GenerateSlug(metadata.TripSlug + "_" + remoteId);

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
                    NavigateToTrip(slug);
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
