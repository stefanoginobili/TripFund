using System.Text.Json;
using System.Text.Json.Serialization;
using TripFund.App.Models;
using TripFund.App.Utilities;
using TripFund.App.Constants;

namespace TripFund.App.Services;

public class ConflictVersion<T>
{
    public T? Data { get; set; }
    public string Author { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string FolderName { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
}

public class LocalStorageService
{
    private readonly string _rootPath;
    private readonly string _tripsPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new DateTimeJsonConverter() }
    };

    public virtual string TripsPath => _tripsPath;
    public virtual string AppDataPath => _rootPath;
    public virtual JsonSerializerOptions JsonOptions => _jsonOptions;

    private AppSettings? _cachedAppSettings;
    private LocalTripRegistry? _cachedTripRegistry;

    public LocalStorageService(string? rootPath = null)
    {
        _rootPath = rootPath ?? FileSystem.AppDataDirectory;
        _tripsPath = Path.Combine(_rootPath, AppConstants.Folders.Trips);
        
        if (!Directory.Exists(_rootPath))
        {
            Directory.CreateDirectory(_rootPath);
        }
        
        if (!Directory.Exists(_tripsPath))
        {
            Directory.CreateDirectory(_tripsPath);
        }
    }

    public virtual LocalTripStorage GetLocalTripStorage(string tripSlug)
    {
        return new LocalTripStorage(this, tripSlug);
    }

    public async Task SaveJsonAtomicAsync<T>(string path, T data)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        await File.WriteAllTextAsync(tempPath, json);
        
        if (File.Exists(path)) File.Delete(path);
        File.Move(tempPath, path);
    }

    public virtual async Task<AppSettings?> GetAppSettingsAsync()
    {
        if (_cachedAppSettings != null) return _cachedAppSettings;

        var path = Path.Combine(_rootPath, AppConstants.Files.AppSettings);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path);
        _cachedAppSettings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
        return _cachedAppSettings;
    }
    public virtual Task SaveAppSettingsAsync(AppSettings settings)
    {
        _cachedAppSettings = settings;
        var path = Path.Combine(_rootPath, AppConstants.Files.AppSettings);
        return SaveJsonAtomicAsync(path, settings);
    }

    public virtual async Task<LocalTripRegistry> GetTripRegistryAsync()
    {
        if (_cachedTripRegistry != null) return _cachedTripRegistry;

        var path = Path.Combine(_rootPath, AppConstants.Files.KnownTrips);
        if (!File.Exists(path)) return new LocalTripRegistry();
        
        try
        {
            var json = await File.ReadAllTextAsync(path);
            _cachedTripRegistry = JsonSerializer.Deserialize<LocalTripRegistry>(json, _jsonOptions) ?? new LocalTripRegistry();
            return _cachedTripRegistry;
        }
        catch (JsonException)
        {
            // If the file is corrupted, return empty registry to allow recovery
            return new LocalTripRegistry();
        }
    }

    public virtual Task SaveTripRegistryAsync(LocalTripRegistry registry)
    {
        _cachedTripRegistry = registry;
        var path = Path.Combine(_rootPath, AppConstants.Files.KnownTrips);
        return SaveJsonAtomicAsync(path, registry);
    }

    public virtual async Task DeleteTripAsync(string tripSlug)
    {
        // Remove from registry
        var registry = await GetTripRegistryAsync();
        if (registry.Trips.ContainsKey(tripSlug))
        {
            registry.Trips.Remove(tripSlug);
            await SaveTripRegistryAsync(registry);
        }

        // Delete local folder
        var tripDir = Path.Combine(_tripsPath, tripSlug);
        if (Directory.Exists(tripDir))
        {
            Directory.Delete(tripDir, true);
        }
    }

    public virtual async Task InitializeInitialImportAsync(string tripSlug)
    {
        var tripDir = Path.Combine(_tripsPath, tripSlug);
        if (!Directory.Exists(tripDir)) Directory.CreateDirectory(tripDir);
        await File.WriteAllTextAsync(Path.Combine(tripDir, AppConstants.Files.InitialImportMarker), DateTime.UtcNow.ToString("O"));
    }

    public virtual void CompleteInitialImport(string tripSlug)
    {
        var markerPath = Path.Combine(_tripsPath, tripSlug, AppConstants.Files.InitialImportMarker);
        if (File.Exists(markerPath)) File.Delete(markerPath);
    }

    public virtual async Task CleanupIncompleteImportsAsync()
    {
        if (!Directory.Exists(_tripsPath)) return;

        var directories = Directory.GetDirectories(_tripsPath);
        foreach (var tripDir in directories)
        {
            var slug = Path.GetFileName(tripDir);
            var markerPath = Path.Combine(tripDir, AppConstants.Files.InitialImportMarker);
            if (File.Exists(markerPath))
            {
                await DeleteTripAsync(slug);
            }
        }
    }

    public virtual async Task CleanupBrokenTripsAsync()
    {
        var registry = await GetTripRegistryAsync();
        var toDelete = new List<string>();

        foreach (var entry in registry.Trips)
        {
            var config = await GetLocalTripStorage(entry.Key).GetTripConfigAsync();
            if (config == null)
            {
                toDelete.Add(entry.Key);
            }
        }

        foreach (var slug in toDelete)
        {
            await DeleteTripAsync(slug);
        }
    }

    public virtual Task CleanupTempFoldersAsync()
    {
        if (!Directory.Exists(_tripsPath)) return Task.CompletedTask;

        var directories = Directory.GetDirectories(_tripsPath);
        foreach (var tripDir in directories)
        {
            var tempPath = Path.Combine(tripDir, AppConstants.Folders.Temp);
            if (Directory.Exists(tempPath))
            {
                try
                {
                    Directory.Delete(tempPath, true);
                }
                catch
                {
                    // Ignore errors during cleanup (e.g. file in use)
                }
            }
        }
        return Task.CompletedTask;
    }
}
