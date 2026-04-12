using TripFund.App.Models;

namespace TripFund.App.Services;

public class CompositeRemoteStorageService : IRemoteStorageService
{
    public event Action<string, bool>? OnSyncStateChanged;
    private readonly OneDriveRemoteStorageService _onedrive;
    private readonly LocalTripStorageService _storage;
    private readonly HashSet<string> _syncingTrips = new();

    public CompositeRemoteStorageService(
        OneDriveRemoteStorageService onedrive,
        LocalTripStorageService storage)
    {
        _onedrive = onedrive;
        _storage = storage;
    }

    public bool IsSyncing(string tripSlug)
    {
        lock (_syncingTrips)
        {
            return _syncingTrips.Contains(tripSlug);
        }
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

    public string? GetRemoteUniqueId(string provider, Dictionary<string, string> parameters)
    {
        if (provider == "onedrive")
        {
            return _onedrive.GetRemoteUniqueId(provider, parameters);
        }

        return null;
    }

    public async Task SynchronizeAsync(string tripSlug)
    {
        lock (_syncingTrips)
        {
            if (_syncingTrips.Contains(tripSlug)) return;
            _syncingTrips.Add(tripSlug);
        }

        OnSyncStateChanged?.Invoke(tripSlug, true);
        try
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
        finally
        {
            lock (_syncingTrips)
            {
                _syncingTrips.Remove(tripSlug);
            }
            // Always ensure we fire false at the very end of the composite operation
            OnSyncStateChanged?.Invoke(tripSlug, false);
        }
    }
}
