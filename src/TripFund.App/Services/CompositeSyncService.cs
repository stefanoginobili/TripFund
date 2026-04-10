using TripFund.App.Models;

namespace TripFund.App.Services;

public class CompositeSyncService : ISyncService
{
    private readonly GoogleDriveSyncService _drive;
    private readonly GitSyncService _git;
    private readonly LocalTripStorageService _storage;

    public CompositeSyncService(GoogleDriveSyncService drive, GitSyncService git, LocalTripStorageService storage)
    {
        _drive = drive;
        _git = git;
        _storage = storage;
    }

    public Task<TripConfig?> GetRemoteTripConfigAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider == "google-drive")
        {
            return _drive.GetRemoteTripConfigAsync(provider, parameters);
        }
        else if (provider == "git")
        {
            return _git.GetRemoteTripConfigAsync(provider, parameters);
        }

        return Task.FromResult<TripConfig?>(null);
    }

    public Task<bool> IsRemoteLocationEmptyAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider == "google-drive")
        {
            return _drive.IsRemoteLocationEmptyAsync(provider, parameters);
        }
        else if (provider == "git")
        {
            return _git.IsRemoteLocationEmptyAsync(provider, parameters);
        }

        return Task.FromResult(false);
    }

    public async Task SyncAsync(string tripSlug)
    {
        var registry = await _storage.GetTripRegistryAsync();
        if (registry.Trips.TryGetValue(tripSlug, out var entry) && entry.RemoteStorage != null)
        {
            if (entry.RemoteStorage.Provider == "google-drive")
            {
                await _drive.SyncAsync(tripSlug);
            }
            else if (entry.RemoteStorage.Provider == "git")
            {
                await _git.SyncAsync(tripSlug);
            }

            // Update last sync time
            entry.RemoteStorage.LastSync = DateTime.Now;
            await _storage.SaveTripRegistryAsync(registry);
        }
    }
}
