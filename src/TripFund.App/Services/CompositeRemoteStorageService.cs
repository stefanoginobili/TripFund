using TripFund.App.Models;

namespace TripFund.App.Services;

public class CompositeRemoteStorageService : IRemoteStorageService
{
    private readonly OneDriveRemoteStorageService _onedrive;
    private readonly LocalTripStorageService _storage;

    public CompositeRemoteStorageService(
        OneDriveRemoteStorageService onedrive,
        LocalTripStorageService storage)
    {
        _onedrive = onedrive;
        _storage = storage;
    }

    public Task<TripConfig?> GetRemoteTripConfigAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider == "onedrive")
        {
            return _onedrive.GetRemoteTripConfigAsync(provider, parameters);
        }

        return Task.FromResult<TripConfig?>(null);
    }

    public Task<bool> IsRemoteLocationEmptyAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider == "onedrive")
        {
            return _onedrive.IsRemoteLocationEmptyAsync(provider, parameters);
        }

        return Task.FromResult(false);
    }

    public async Task SynchronizeAsync(string tripSlug)
    {
        var registry = await _storage.GetTripRegistryAsync();
        if (registry.Trips.TryGetValue(tripSlug, out var entry) && entry.RemoteStorage != null)
        {
            if (entry.RemoteStorage.Provider == "onedrive")
            {
                await _onedrive.SynchronizeAsync(tripSlug);
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
