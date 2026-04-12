using System.Text.Json;
using System.Text.Json.Serialization;
using TripFund.App.Models;

namespace TripFund.App.Services;

public class TransactionConflictException : Exception
{
    public string TransactionId { get; }
    public List<string> ConflictingDeviceIds { get; }

    public TransactionConflictException(string transactionId, List<string> conflictingDeviceIds)
        : base($"Conflict detected in transaction {transactionId} between: {string.Join(", ", conflictingDeviceIds)}")
    {
        TransactionId = transactionId;
        ConflictingDeviceIds = conflictingDeviceIds;
    }
}

public class LocalTripStorageService
{
    private readonly string _rootPath;
    private readonly string _tripsPath;
    private readonly VersionedStorageEngine _engine = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public virtual string TripsPath => _tripsPath;

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
        var metadataPath = Path.Combine(_tripsPath, tripSlug, "metadata");
        if (!Directory.Exists(metadataPath)) return null;

        var latestVersions = _engine.GetLatestVersionFolders(metadataPath);
        if (latestVersions.Count == 0) return null;

        var latest = latestVersions[0]; // In case of conflict, we'll return the first for now or handle later
        var configFilePath = Path.Combine(metadataPath, latest.FolderName, "trip_config.json");
        
        if (!File.Exists(configFilePath)) return null;
        
        var json = await File.ReadAllTextAsync(configFilePath);
        return JsonSerializer.Deserialize<TripConfig>(json, _jsonOptions);
    }

    public virtual async Task SaveTripConfigAsync(string tripSlug, TripConfig config, string deviceId, bool isResolve = false)
    {
        var metadataPath = Path.Combine(_tripsPath, tripSlug, "metadata");
        if (!Directory.Exists(metadataPath)) Directory.CreateDirectory(metadataPath);

        var settings = await GetAppSettingsAsync();
        config.Author = settings?.AuthorName ?? "Unknown";

        if (config.CreatedAt == default) config.CreatedAt = DateTime.UtcNow;
        config.UpdatedAt = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(config, _jsonOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        var changedFiles = new Dictionary<string, byte[]> { { "trip_config.json", bytes } };
        
        var kind = isResolve ? CommitKind.Res : ( _engine.GetVersionFolders(metadataPath).Count == 0 ? CommitKind.New : CommitKind.Upd);

        await _engine.CommitAsync(metadataPath, deviceId, kind, changedFiles);
    }

    public virtual async Task<List<Transaction>> GetTransactionsAsync(string tripSlug)
    {
        var transactionsDir = Path.Combine(_tripsPath, tripSlug, "transactions");
        if (!Directory.Exists(transactionsDir)) return new List<Transaction>();

        var result = new List<Transaction>();
        foreach (var transRoot in Directory.GetDirectories(transactionsDir))
        {
            var transactionId = Path.GetFileName(transRoot);
            var transaction = await GetLatestTransactionVersionAsync(tripSlug, transactionId);
            if (transaction != null)
            {
                result.Add(transaction);
            }
        }

        return result;
    }

    public virtual async Task<TransactionVersionInfo?> GetLatestTransactionVersionWithMetadataAsync(string tripSlug, string transactionId)
    {
        var transRoot = Path.Combine(_tripsPath, tripSlug, "transactions", transactionId);
        if (!Directory.Exists(transRoot)) return null;

        var latestVersions = _engine.GetLatestVersionFolders(transRoot);
        if (latestVersions.Count == 0) return null;

        if (latestVersions.Count > 1)
        {
            throw new TransactionConflictException(transactionId, latestVersions.Select(v => v.DeviceId).ToList());
        }

        var latest = latestVersions[0];
        var latestDirPath = Path.Combine(transRoot, latest.FolderName);

        if (File.Exists(Path.Combine(latestDirPath, ".deleted")))
        {
            return new TransactionVersionInfo { VersionFolderName = latest.FolderName, IsDeleted = true };
        }

        var dataPath = Path.Combine(latestDirPath, "data.json");
        if (!File.Exists(dataPath)) return null;

        var json = await File.ReadAllTextAsync(dataPath);
        var transaction = JsonSerializer.Deserialize<Transaction>(json, _jsonOptions);
        if (transaction == null) return null;

        return new TransactionVersionInfo
        {
            Transaction = transaction,
            VersionFolderName = latest.FolderName
        };
    }

    public virtual async Task<Transaction?> GetLatestTransactionVersionAsync(string tripSlug, string transactionId)
    {
        var info = await GetLatestTransactionVersionWithMetadataAsync(tripSlug, transactionId);
        if (info == null || info.IsDeleted) return null;
        return info.Transaction;
    }

    public virtual async Task<string?> GetAttachmentPath(string tripSlug, string transactionId, string fileName)
    {
        var info = await GetLatestTransactionVersionWithMetadataAsync(tripSlug, transactionId);
        if (info == null || info.IsDeleted || info.Transaction == null) return null;

        var path = Path.Combine(_tripsPath, tripSlug, "transactions", transactionId, info.VersionFolderName, fileName);
        return File.Exists(path) ? path : null;
    }

    public class TransactionVersionInfo
    {
        public Transaction? Transaction { get; set; }
        public string VersionFolderName { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
    }

    public virtual async Task SaveTransactionAsync(string tripSlug, Transaction transaction, string deviceId, bool isDelete = false, Dictionary<string, byte[]>? attachments = null)
    {
        var transRoot = Path.Combine(_tripsPath, tripSlug, "transactions", transaction.Id);
        if (!Directory.Exists(transRoot)) Directory.CreateDirectory(transRoot);

        CommitKind kind = _engine.GetVersionFolders(transRoot).Count == 0 ? CommitKind.New : (isDelete ? CommitKind.Del : CommitKind.Upd);

        var changedFiles = new Dictionary<string, byte[]>();
        List<string>? deletedFiles = null;
        string? deletedInfo = null;

        var settings = await GetAppSettingsAsync();
        var author = settings?.AuthorName ?? "Unknown";

        if (isDelete)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
            deletedInfo = $"author={author}\ndeletedAt={timestamp}";
        }
        else
        {
            if (kind == CommitKind.Upd)
            {
                var latest = await GetLatestTransactionVersionAsync(tripSlug, transaction.Id);
                if (latest != null)
                {
                    // Find attachments that are in the previous version but NOT in the new transaction.Attachments
                    deletedFiles = latest.Attachments
                        .Where(a => !transaction.Attachments.Any(ta => ta.Name == a.Name))
                        .Select(a => a.Name)
                        .ToList();
                }
            }

            transaction.Author = author;
            if (transaction.CreatedAt == default) transaction.CreatedAt = DateTime.UtcNow;
            transaction.UpdatedAt = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(transaction, _jsonOptions);
            changedFiles["data.json"] = System.Text.Encoding.UTF8.GetBytes(json);
            
            if (attachments != null)
            {
                foreach (var attachment in attachments)
                {
                    changedFiles[attachment.Key] = attachment.Value;
                }
            }
        }

        await _engine.CommitAsync(transRoot, deviceId, kind, changedFiles, deletedFiles: deletedFiles, deletedInfo: deletedInfo);
    }

    public virtual async Task<Dictionary<string, Transaction>> GetConflictingVersionsAsync(string tripSlug, string transactionId)
    {
        var transRoot = Path.Combine(_tripsPath, tripSlug, "transactions", transactionId);
        var latestVersions = _engine.GetLatestVersionFolders(transRoot);

        var result = new Dictionary<string, Transaction>();
        foreach (var v in latestVersions)
        {
            var latestDirPath = Path.Combine(transRoot, v.FolderName);
            if (File.Exists(Path.Combine(latestDirPath, ".deleted"))) continue;

            var dataPath = Path.Combine(latestDirPath, "data.json");
            if (File.Exists(dataPath))
            {
                var json = await File.ReadAllTextAsync(dataPath);
                var trans = JsonSerializer.Deserialize<Transaction>(json, _jsonOptions);
                if (trans != null) result[v.DeviceId] = trans;
            }
        }
        return result;
    }

    public virtual async Task ResolveConflictAsync(string tripSlug, Transaction resolvedTransaction, string deviceId)
    {
        var transRoot = Path.Combine(_tripsPath, tripSlug, "transactions", resolvedTransaction.Id);
        
        List<string>? deletedFiles = null;
        var conflicts = await GetConflictingVersionsAsync(tripSlug, resolvedTransaction.Id);
        if (conflicts.Any())
        {
            // We take the first one as a baseline for what might have been deleted
            var baseline = conflicts.Values.First();
            deletedFiles = baseline.Attachments
                .Where(a => !resolvedTransaction.Attachments.Any(ta => ta.Name == a.Name))
                .Select(a => a.Name)
                .ToList();
        }

        var settings = await GetAppSettingsAsync();
        resolvedTransaction.Author = settings?.AuthorName ?? "Unknown";
        resolvedTransaction.UpdatedAt = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(resolvedTransaction, _jsonOptions);
        var changedFiles = new Dictionary<string, byte[]> { { "data.json", System.Text.Encoding.UTF8.GetBytes(json) } };

        await _engine.CommitAsync(transRoot, deviceId, CommitKind.Res, changedFiles, deletedFiles: deletedFiles);
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
}

