using System.Text.Json;
using System.Text.Json.Serialization;
using TripFund.App.Models;

namespace TripFund.App.Services;

public class TransactionConflictException : Exception
{
    public string TransactionId { get; }
    public List<string> ConflictingUserSlugs { get; }

    public TransactionConflictException(string transactionId, List<string> conflictingUserSlugs)
        : base($"Conflict detected in transaction {transactionId} between: {string.Join(", ", conflictingUserSlugs)}")
    {
        TransactionId = transactionId;
        ConflictingUserSlugs = conflictingUserSlugs;
    }
}

public class LocalTripStorageService
{
    private readonly string _rootPath;
    private readonly string _tripsPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public LocalTripStorageService(string? rootPath = null)
    {
        _rootPath = rootPath ?? FileSystem.AppDataDirectory;
        _tripsPath = Path.Combine(_rootPath, "trips");
        
        if (!Directory.Exists(_rootPath))
        {
            Directory.CreateDirectory(_rootPath);
        }
        
        if (!Directory.Exists(_tripsPath))
        {
            Directory.CreateDirectory(_tripsPath);
        }
    }

    public virtual async Task<AppSettings?> GetAppSettingsAsync()
    {
        var path = Path.Combine(_rootPath, "app_settings.json");
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
    }

    public virtual async Task SaveAppSettingsAsync(AppSettings settings)
    {
        var path = Path.Combine(_rootPath, "app_settings.json");
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    public virtual async Task<LocalTripRegistry> GetTripRegistryAsync()
    {
        var path = Path.Combine(_rootPath, "known_trips.json");
        if (!File.Exists(path)) return new LocalTripRegistry();
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<LocalTripRegistry>(json, _jsonOptions) ?? new LocalTripRegistry();
    }

    public virtual async Task SaveTripRegistryAsync(LocalTripRegistry registry)
    {
        var path = Path.Combine(_rootPath, "known_trips.json");
        var json = JsonSerializer.Serialize(registry, _jsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    public virtual async Task<TripConfig?> GetTripConfigAsync(string tripSlug)
    {
        var path = Path.Combine(_tripsPath, tripSlug, "trip_config.json");
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<TripConfig>(json, _jsonOptions);
    }

    public virtual async Task SaveTripConfigAsync(string tripSlug, TripConfig config)
    {
        var tripDir = Path.Combine(_tripsPath, tripSlug);
        if (!Directory.Exists(tripDir)) Directory.CreateDirectory(tripDir);
        var path = Path.Combine(tripDir, "trip_config.json");
        var json = JsonSerializer.Serialize(config, _jsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    public virtual async Task<List<Transaction>> GetTransactionsAsync(string tripSlug)
    {
        var transactionsDir = Path.Combine(_tripsPath, tripSlug, "Transactions");
        if (!Directory.Exists(transactionsDir)) return new List<Transaction>();

        var result = new List<Transaction>();
        foreach (var transDir in Directory.GetDirectories(transactionsDir))
        {
            var transactionId = Path.GetFileName(transDir);
            var transaction = await GetLatestTransactionVersionAsync(tripSlug, transactionId);
            if (transaction != null)
            {
                result.Add(transaction);
            }
        }

        return result;
    }

    public virtual async Task<Transaction?> GetLatestTransactionVersionAsync(string tripSlug, string transactionId)
    {
        var transDir = Path.Combine(_tripsPath, tripSlug, "Transactions", transactionId);
        if (!Directory.Exists(transDir)) return null;

        var versionDirs = Directory.GetDirectories(transDir)
            .Select(Path.GetFileName)
            .Where(n => n != null && !n.StartsWith("_"))
            .Select(n => ParseVersionFolderName(n!))
            .ToList();

        if (versionDirs.Count == 0) return null;

        var maxVersionNum = versionDirs.Max(v => v.Version);
        var topVersions = versionDirs.Where(v => v.Version == maxVersionNum).ToList();

        if (topVersions.Count > 1)
        {
            throw new TransactionConflictException(transactionId, topVersions.Select(v => v.UserSlug).ToList());
        }

        var latest = topVersions[0];
        var latestDirPath = Path.Combine(transDir, latest.FolderName);

        if (File.Exists(Path.Combine(latestDirPath, ".deleted")))
        {
            return null;
        }

        var dataPath = Path.Combine(latestDirPath, "data.json");
        if (!File.Exists(dataPath)) return null;

        var json = await File.ReadAllTextAsync(dataPath);
        return JsonSerializer.Deserialize<Transaction>(json, _jsonOptions);
    }

    public virtual async Task SaveTransactionAsync(string tripSlug, Transaction transaction, string authorSlug, bool isDelete = false)
    {
        var transDir = Path.Combine(_tripsPath, tripSlug, "Transactions", transaction.Id);
        if (!Directory.Exists(transDir)) Directory.CreateDirectory(transDir);

        var existingVersions = Directory.GetDirectories(transDir)
            .Select(Path.GetFileName)
            .Where(n => n != null && !n.StartsWith("_"))
            .Select(n => ParseVersionFolderName(n!))
            .ToList();

        int nextVersionNum = existingVersions.Count == 0 ? 1 : existingVersions.Max(v => v.Version) + 1;
        var nextFolderName = $"{nextVersionNum:D3}_{authorSlug}";
        var nextDirPath = Path.Combine(transDir, nextFolderName);
        Directory.CreateDirectory(nextDirPath);

        if (isDelete)
        {
            await File.WriteAllTextAsync(Path.Combine(nextDirPath, ".deleted"), "");
        }
        else
        {
            var json = JsonSerializer.Serialize(transaction, _jsonOptions);
            await File.WriteAllTextAsync(Path.Combine(nextDirPath, "data.json"), json);
        }
    }

    public virtual async Task<Dictionary<string, Transaction>> GetConflictingVersionsAsync(string tripSlug, string transactionId)
    {
        var transDir = Path.Combine(_tripsPath, tripSlug, "Transactions", transactionId);
        var versionDirs = Directory.GetDirectories(transDir)
            .Select(Path.GetFileName)
            .Where(n => n != null && !n.StartsWith("_"))
            .Select(n => ParseVersionFolderName(n!))
            .ToList();

        var latestByUser = versionDirs
            .GroupBy(v => v.UserSlug)
            .Select(g => g.OrderByDescending(v => v.Version).First())
            .ToList();

        var result = new Dictionary<string, Transaction>();
        foreach (var v in latestByUser)
        {
            var latestDirPath = Path.Combine(transDir, v.FolderName);
            if (File.Exists(Path.Combine(latestDirPath, ".deleted"))) continue;

            var dataPath = Path.Combine(latestDirPath, "data.json");
            if (File.Exists(dataPath))
            {
                var json = await File.ReadAllTextAsync(dataPath);
                var trans = JsonSerializer.Deserialize<Transaction>(json, _jsonOptions);
                if (trans != null) result[v.UserSlug] = trans;
            }
        }
        return result;
    }

    public virtual async Task ResolveConflictAsync(string tripSlug, Transaction resolvedTransaction, string authorSlug)
    {
        var transDir = Path.Combine(_tripsPath, tripSlug, "Transactions", resolvedTransaction.Id);
        
        var allActiveVersionDirs = Directory.GetDirectories(transDir)
            .Select(Path.GetFileName)
            .Where(n => n != null && !n.StartsWith("_"))
            .Select(n => ParseVersionFolderName(n!))
            .ToList();

        int maxVersionAcrossThreads = allActiveVersionDirs.Max(v => v.Version);

        // Archive all active folders
        foreach (var v in allActiveVersionDirs)
        {
            var oldPath = Path.Combine(transDir, v.FolderName);
            var newPath = Path.Combine(transDir, "_" + v.FolderName);
            Directory.Move(oldPath, newPath);
        }

        // Create new resolved version
        int nextVersionNum = maxVersionAcrossThreads + 1;
        var nextFolderName = $"{nextVersionNum:D3}_{authorSlug}";
        var nextDirPath = Path.Combine(transDir, nextFolderName);
        Directory.CreateDirectory(nextDirPath);

        var json = JsonSerializer.Serialize(resolvedTransaction, _jsonOptions);
        await File.WriteAllTextAsync(Path.Combine(nextDirPath, "data.json"), json);
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

    private (int Version, string UserSlug, string FolderName) ParseVersionFolderName(string folderName)
    {
        var parts = folderName.Split('_');
        if (parts.Length < 2 || !int.TryParse(parts[0], out int version))
        {
            return (0, folderName, folderName);
        }
        return (version, folderName.Substring(parts[0].Length + 1), folderName);
    }
}
