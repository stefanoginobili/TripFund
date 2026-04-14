using System.Text.Json;
using System.Text.Json.Serialization;
using TripFund.App.Models;

namespace TripFund.App.Services;

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
    public virtual string AppDataPath => _rootPath;

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
        var metadataRoot = Path.Combine(_tripsPath, tripSlug, "transactions", transactionId, "metadata");
        if (!Directory.Exists(metadataRoot)) return null;

        var latestVersions = _engine.GetLatestVersionFolders(metadataRoot);
        if (latestVersions.Count == 0) return null;

        if (latestVersions.Count > 1)
        {
            var diverging = latestVersions.Select(v => v.FolderName).ToList();
            var baseVer = _engine.GetBaseVersionFolder(metadataRoot, latestVersions[0].Sequence);
            throw new TransactionConflictException(transactionId, diverging, baseVer);
        }

        var latest = latestVersions[0];
        var latestDirPath = Path.Combine(metadataRoot, latest.FolderName);

        if (File.Exists(Path.Combine(latestDirPath, ".deleted")))
        {
            return new TransactionVersionInfo { VersionFolderName = latest.FolderName, IsDeleted = true };
        }

        var dataPath = Path.Combine(latestDirPath, "transaction_detail.json");
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

    public virtual async Task<string?> GetAttachmentPath(string tripSlug, string transactionId, string attachmentName)
    {
        var info = await GetLatestTransactionVersionWithMetadataAsync(tripSlug, transactionId);
        if (info == null || info.IsDeleted || info.Transaction == null) return null;

        var attachment = info.Transaction.Attachments.FirstOrDefault(a => a.Name == attachmentName);
        if (attachment == null) return null;

        var path = Path.Combine(_tripsPath, tripSlug, "transactions", transactionId, "attachments", attachment.Name, attachment.OriginalName);
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

        var metadataRoot = Path.Combine(transRoot, "metadata");
        if (!Directory.Exists(metadataRoot)) Directory.CreateDirectory(metadataRoot);

        CommitKind kind = _engine.GetVersionFolders(metadataRoot).Count == 0 ? CommitKind.New : (isDelete ? CommitKind.Del : CommitKind.Upd);

        var changedFiles = new Dictionary<string, byte[]>();
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
            transaction.Author = author;
            if (transaction.CreatedAt == default)
            {
                var existing = await GetLatestTransactionVersionAsync(tripSlug, transaction.Id);
                if (existing != null) transaction.CreatedAt = existing.CreatedAt;
                else transaction.CreatedAt = DateTime.UtcNow;
            }
            transaction.UpdatedAt = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(transaction, _jsonOptions);
            changedFiles["transaction_detail.json"] = System.Text.Encoding.UTF8.GetBytes(json);
            
            // Save attachments to the unversioned 'attachments' folder
            if (attachments != null)
            {
                var attachmentsDir = Path.Combine(transRoot, "attachments");
                if (!Directory.Exists(attachmentsDir)) Directory.CreateDirectory(attachmentsDir);

                foreach (var attachmentEntry in attachments)
                {
                    var attachmentName = attachmentEntry.Key;
                    var content = attachmentEntry.Value;

                    // Match with Transaction.Attachments to get the OriginalName
                    var attMetadata = transaction.Attachments.FirstOrDefault(a => a.Name == attachmentName);
                    if (attMetadata != null)
                    {
                        var leafDir = Path.Combine(attachmentsDir, attachmentName);
                        if (!Directory.Exists(leafDir)) Directory.CreateDirectory(leafDir);

                        var filePath = Path.Combine(leafDir, attMetadata.OriginalName);
                        if (!File.Exists(filePath))
                        {
                            await File.WriteAllBytesAsync(filePath, content);
                        }
                    }
                }
            }
        }

        // Note: deletedFiles is no longer needed because attachments are not in the versioned folder
        await _engine.CommitAsync(metadataRoot, deviceId, kind, changedFiles, deletedInfo: deletedInfo);
    }

    public virtual async Task<Dictionary<string, Transaction>> GetConflictingVersionsAsync(string tripSlug, string transactionId)
    {
        var metadataRoot = Path.Combine(_tripsPath, tripSlug, "transactions", transactionId, "metadata");
        var latestVersions = _engine.GetLatestVersionFolders(metadataRoot);

        var result = new Dictionary<string, Transaction>();
        foreach (var v in latestVersions)
        {
            var latestDirPath = Path.Combine(metadataRoot, v.FolderName);
            if (File.Exists(Path.Combine(latestDirPath, ".deleted"))) continue;

            var dataPath = Path.Combine(latestDirPath, "transaction_detail.json");
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
        var metadataRoot = Path.Combine(_tripsPath, tripSlug, "transactions", resolvedTransaction.Id, "metadata");
        
        var settings = await GetAppSettingsAsync();
        resolvedTransaction.Author = settings?.AuthorName ?? "Unknown";
        
        IReadOnlyList<VersionFolderInfo> latestVersions;
        if (resolvedTransaction.CreatedAt == default)
        {
            // Try to find the original CreatedAt from one of the conflicting versions or previous history
            // We don't use GetLatestTransactionVersionWithMetadataAsync because it throws on conflict
            latestVersions = _engine.GetLatestVersionFolders(metadataRoot);
            if (latestVersions.Count > 0)
            {
                var latest = latestVersions[0];
                var latestDirPath = Path.Combine(metadataRoot, latest.FolderName);
                var dataPath = Path.Combine(latestDirPath, "transaction_detail.json");
                if (File.Exists(dataPath))
                {
                    var prevJson = await File.ReadAllTextAsync(dataPath);
                    var prev = JsonSerializer.Deserialize<Transaction>(prevJson, _jsonOptions);
                    if (prev != null) resolvedTransaction.CreatedAt = prev.CreatedAt;
                }
            }

            // Fallback to searching all versions if not found above
            if (resolvedTransaction.CreatedAt == default)
            {
                var allVersions = _engine.GetVersionFolders(metadataRoot).OrderByDescending(v => v.Sequence);
                foreach (var v in allVersions)
                {
                    var dirPath = Path.Combine(metadataRoot, v.FolderName);
                    var dataPath = Path.Combine(dirPath, "transaction_detail.json");
                    if (File.Exists(dataPath))
                    {
                        var prevJson = await File.ReadAllTextAsync(dataPath);
                        var prev = JsonSerializer.Deserialize<Transaction>(prevJson, _jsonOptions);
                        if (prev != null && prev.CreatedAt != default)
                        {
                            resolvedTransaction.CreatedAt = prev.CreatedAt;
                            break;
                        }
                    }
                }
            }
            
            if (resolvedTransaction.CreatedAt == default)
            {
                resolvedTransaction.CreatedAt = DateTime.UtcNow;
            }
        }
        else
        {
            latestVersions = _engine.GetLatestVersionFolders(metadataRoot);
        }
        
        resolvedTransaction.UpdatedAt = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(resolvedTransaction, _jsonOptions);
        var changedFiles = new Dictionary<string, byte[]> { { "transaction_detail.json", System.Text.Encoding.UTF8.GetBytes(json) } };

        var resolvedFolders = latestVersions.Select(v => v.FolderName).ToList();

        await _engine.CommitAsync(metadataRoot, deviceId, CommitKind.Res, changedFiles, resolvedFolders: resolvedFolders);
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

