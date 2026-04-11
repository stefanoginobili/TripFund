using TripFund.App.Models;

namespace TripFund.App.Services;

public class CompositeRemoteStorageService : IRemoteStorageService
{
    private readonly GoogleDriveRemoteStorageService _drive;
    private readonly LocalTripStorageService _storage;

    public CompositeRemoteStorageService(GoogleDriveRemoteStorageService drive, LocalTripStorageService storage)
    {
        _drive = drive;
        _storage = storage;
    }

    public Task<TripConfig?> GetRemoteTripConfigAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider == "google-drive")
        {
            return _drive.GetRemoteTripConfigAsync(provider, parameters);
        }

        return Task.FromResult<TripConfig?>(null);
    }

    public Task<bool> IsRemoteLocationEmptyAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider == "google-drive")
        {
            return _drive.IsRemoteLocationEmptyAsync(provider, parameters);
        }

        return Task.FromResult(false);
    }

    public async Task SynchronizeAsync(string tripSlug)
    {
        var registry = await _storage.GetTripRegistryAsync();
        if (registry.Trips.TryGetValue(tripSlug, out var entry) && entry.RemoteStorage != null)
        {
            if (entry.RemoteStorage.Provider == "google-drive")
            {
                await _drive.SynchronizeAsync(tripSlug);
            }

            // Reload registry to get updates from the provider (hasConflicts, readonly)
            registry = await _storage.GetTripRegistryAsync();
            if (registry.Trips.TryGetValue(tripSlug, out var updatedEntry) && updatedEntry.RemoteStorage != null)
            {
                updatedEntry.RemoteStorage.LastSynchronized = DateTime.Now;
                await _storage.SaveTripRegistryAsync(registry);
            }
        }
    }
}
