using TripFund.App.Models;
using Microsoft.Maui.Devices;
using Microsoft.Maui.ApplicationModel;

namespace TripFund.App.Services;

public class CompositeRemoteStorageService : IRemoteStorageService
{
    public event Action<string, bool>? OnSyncStateChanged;
    private readonly OneDriveRemoteStorageService _onedrive;
    private readonly LocalStorageService _storage;
    private readonly HashSet<string> _syncingTrips = new();

    public CompositeRemoteStorageService(
        OneDriveRemoteStorageService onedrive,
        LocalStorageService storage)
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

    public Task<RemoteTripMetadata?> GetRemoteTripMetadataAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider == "onedrive")
        {
            return _onedrive.GetRemoteTripMetadataAsync(provider, parameters);
        }

        return Task.FromResult<RemoteTripMetadata?>(null);
    }

    public Task InitializeRemoteLocationAsync(string tripSlug, string provider, Dictionary<string, string> parameters)
    {
        if (provider == "onedrive")
        {
            return _onedrive.InitializeRemoteLocationAsync(tripSlug, provider, parameters);
        }

        return Task.CompletedTask;
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

        UpdateKeepScreenOn();
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

                // Update sync state with last success timestamp
                var syncState = await _storage.GetLocalTripStorage(tripSlug).GetSyncStateAsync();
                syncState.Sync.LastSuccessAt = DateTime.UtcNow;
                await _storage.GetLocalTripStorage(tripSlug).SaveSyncStateAsync(syncState);
            }
        }
        finally
        {
            lock (_syncingTrips)
            {
                _syncingTrips.Remove(tripSlug);
            }
            UpdateKeepScreenOn();
            // Always ensure we fire false at the very end of the composite operation
            OnSyncStateChanged?.Invoke(tripSlug, false);
        }
    }

    private void UpdateKeepScreenOn()
    {
        try
        {
            bool shouldKeepOn;
            lock (_syncingTrips)
            {
                shouldKeepOn = _syncingTrips.Count > 0;
            }

            // Always call on main thread for MAUI Essentials
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    // DeviceDisplay.Current.KeepScreenOn is a cross-platform API for Android and iOS
                    DeviceDisplay.Current.KeepScreenOn = shouldKeepOn;
                }
                catch (Exception ex)
                {
                    // Fail silently as it might be called on non-supported platforms or in tests
                    System.Diagnostics.Debug.WriteLine($"Failed to set KeepScreenOn: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            // Fail silently as it might be called on non-supported platforms or in tests
            System.Diagnostics.Debug.WriteLine($"UpdateKeepScreenOn error: {ex.Message}");
        }
    }
}
