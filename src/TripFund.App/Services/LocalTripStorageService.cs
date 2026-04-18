using System.Text.Json;
using System.Text.Json.Serialization;
using TripFund.App.Models;

namespace TripFund.App.Services;

public class LocalTripStorageService
{
    private readonly string _rootPath;
    private readonly string _tripsPath;
    private const string INITIAL_IMPORT_MARKER = ".initial_import";
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
        var tempPath = path + ".tmp";
        
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        await File.WriteAllTextAsync(tempPath, json);
        
        if (File.Exists(path)) File.Delete(path);
        File.Move(tempPath, path);
    }

    public virtual async Task<LocalTripRegistry> GetTripRegistryAsync()
    {
        var path = Path.Combine(_rootPath, "known_trips.json");
        if (!File.Exists(path)) return new LocalTripRegistry();
        
        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<LocalTripRegistry>(json, _jsonOptions) ?? new LocalTripRegistry();
        }
        catch (JsonException)
        {
            // If the file is corrupted, return empty registry to allow recovery
            return new LocalTripRegistry();
        }
    }

    public virtual async Task SaveTripRegistryAsync(LocalTripRegistry registry)
    {
        var path = Path.Combine(_rootPath, "known_trips.json");
        var tempPath = path + ".tmp";

        var json = JsonSerializer.Serialize(registry, _jsonOptions);
        await File.WriteAllTextAsync(tempPath, json);
        
        if (File.Exists(path)) File.Delete(path);
        File.Move(tempPath, path);
    }

    public virtual async Task<TripConfig?> GetTripConfigAsync(string tripSlug)
    {
        var configPath = Path.Combine(_tripsPath, tripSlug, "config_versioned");
        if (!Directory.Exists(configPath)) return null;

        var latestVersions = _engine.GetLatestVersionFolders(configPath);
        if (latestVersions.Count == 0) return null;

        var latest = latestVersions[0];
        var leaf = new LocalLeafFolder(Path.Combine(configPath, latest.FolderName));
        
        if (await leaf.IsDataEmptyAsync()) return null;
        
        var bytes = await leaf.ReadDataFileAsync("trip_config.json");
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        return JsonSerializer.Deserialize<TripConfig>(json, _jsonOptions);
    }

    public virtual async Task SaveTripConfigAsync(string tripSlug, TripConfig config, string deviceId, bool isResolve = false)
    {
        var configPath = Path.Combine(_tripsPath, tripSlug, "config_versioned");
        if (!Directory.Exists(configPath)) Directory.CreateDirectory(configPath);

        // Ensure the config.Id is always the tripSlug (local folder name)
        config.Id = tripSlug;

        var settings = await GetAppSettingsAsync();
        var author = settings?.AuthorName ?? "Unknown";
        config.Author = author;

        if (config.CreatedAt == default) config.CreatedAt = DateTime.UtcNow;
        config.UpdatedAt = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(config, _jsonOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        var changedFiles = new Dictionary<string, byte[]> { { "trip_config.json", bytes } };
        
        var kind = isResolve ? CommitKind.Res : ( _engine.GetVersionFolders(configPath).Count == 0 ? CommitKind.New : CommitKind.Upd);

        var metadata = new Dictionary<string, string>
        {
            { "author", author },
            { "device", deviceId },
            { "timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") }
        };

        await _engine.CommitAsync(configPath, deviceId, kind, changedFiles, metadata: metadata);
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

    public virtual async Task<TransactionVersionInfo?> GetLatestTransactionVersionWithDetailsAsync(string tripSlug, string transactionId)
    {
        var detailsRoot = Path.Combine(_tripsPath, tripSlug, "transactions", transactionId, "details_versioned");
        if (!Directory.Exists(detailsRoot)) return null;

        var latestVersions = _engine.GetLatestVersionFolders(detailsRoot);
        if (latestVersions.Count == 0) return null;

        if (latestVersions.Count > 1)
        {
            var diverging = latestVersions.Select(v => v.FolderName).ToList();
            var baseVer = _engine.GetBaseVersionFolder(detailsRoot, latestVersions[0].Sequence);
            throw new TransactionConflictException(transactionId, diverging, baseVer);
        }

        var latest = latestVersions[0];
        var leaf = new LocalLeafFolder(Path.Combine(detailsRoot, latest.FolderName));

        if (await leaf.IsDataEmptyAsync())
        {
            return new TransactionVersionInfo { VersionFolderName = latest.FolderName, IsDeleted = true };
        }

        var bytes = await leaf.ReadDataFileAsync("transaction_details.json");
        var json = System.Text.Encoding.UTF8.GetString(bytes);
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
        var info = await GetLatestTransactionVersionWithDetailsAsync(tripSlug, transactionId);
        if (info == null || info.IsDeleted) return null;
        return info.Transaction;
    }

    public virtual async Task<string?> GetAttachmentPath(string tripSlug, string transactionId, string attachmentName)
    {
        var info = await GetLatestTransactionVersionWithDetailsAsync(tripSlug, transactionId);
        if (info == null || info.IsDeleted || info.Transaction == null) return null;

        var attachment = info.Transaction.Attachments.FirstOrDefault(a => a.Name == attachmentName);
        if (attachment == null) return null;

        var leafPath = Path.Combine(_tripsPath, tripSlug, "transactions", transactionId, "attachments", attachment.Name);
        var path = Path.Combine(leafPath, ".data", attachment.OriginalName);
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

        var detailsRoot = Path.Combine(transRoot, "details_versioned");
        if (!Directory.Exists(detailsRoot)) Directory.CreateDirectory(detailsRoot);

        CommitKind kind = _engine.GetVersionFolders(detailsRoot).Count == 0 ? CommitKind.New : (isDelete ? CommitKind.Del : CommitKind.Upd);

        var changedFiles = new Dictionary<string, byte[]>();

        var settings = await GetAppSettingsAsync();
        var author = settings?.AuthorName ?? "Unknown";

        var metadata = new Dictionary<string, string>
        {
            { "author", author },
            { "device", deviceId },
            { "timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") }
        };

        if (!isDelete)
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
            changedFiles["transaction_details.json"] = System.Text.Encoding.UTF8.GetBytes(json);
            
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
                        var attLeaf = new LocalLeafFolder(leafDir);
                        await attLeaf.WriteDataFileAsync(attMetadata.OriginalName, content);
                        await attLeaf.SaveMetadataAsync(metadata);
                    }
                }
            }
        }

        await _engine.CommitAsync(detailsRoot, deviceId, kind, changedFiles, metadata: metadata);
    }

    public virtual async Task<Dictionary<string, Transaction>> GetConflictingVersionsAsync(string tripSlug, string transactionId)
    {
        var detailsRoot = Path.Combine(_tripsPath, tripSlug, "transactions", transactionId, "details_versioned");
        var latestVersions = _engine.GetLatestVersionFolders(detailsRoot);

        var result = new Dictionary<string, Transaction>();
        foreach (var v in latestVersions)
        {
            var leaf = new LocalLeafFolder(Path.Combine(detailsRoot, v.FolderName));
            if (await leaf.IsDataEmptyAsync()) continue;

            try 
            {
                var bytes = await leaf.ReadDataFileAsync("transaction_details.json");
                var json = System.Text.Encoding.UTF8.GetString(bytes);
                var trans = JsonSerializer.Deserialize<Transaction>(json, _jsonOptions);
                if (trans != null) result[v.DeviceId] = trans;
            }
            catch (FileNotFoundException) { }
        }
        return result;
    }

    public virtual async Task ResolveConflictAsync(string tripSlug, Transaction resolvedTransaction, string deviceId)
    {
        var detailsRoot = Path.Combine(_tripsPath, tripSlug, "transactions", resolvedTransaction.Id, "details_versioned");
        
        var settings = await GetAppSettingsAsync();
        var author = settings?.AuthorName ?? "Unknown";
        resolvedTransaction.Author = author;
        
        IReadOnlyList<VersionFolderInfo> latestVersions;
        if (resolvedTransaction.CreatedAt == default)
        {
            latestVersions = _engine.GetLatestVersionFolders(detailsRoot);
            if (latestVersions.Count > 0)
            {
                var latest = latestVersions[0];
                var leaf = new LocalLeafFolder(Path.Combine(detailsRoot, latest.FolderName));
                try
                {
                    var prevBytes = await leaf.ReadDataFileAsync("transaction_details.json");
                    var prevJson = System.Text.Encoding.UTF8.GetString(prevBytes);
                    var prev = JsonSerializer.Deserialize<Transaction>(prevJson, _jsonOptions);
                    if (prev != null) resolvedTransaction.CreatedAt = prev.CreatedAt;
                }
                catch (FileNotFoundException) { }
            }

            if (resolvedTransaction.CreatedAt == default)
            {
                var allVersions = _engine.GetVersionFolders(detailsRoot).OrderByDescending(v => v.Sequence);
                foreach (var v in allVersions)
                {
                    var leaf = new LocalLeafFolder(Path.Combine(detailsRoot, v.FolderName));
                    try
                    {
                        var prevBytes = await leaf.ReadDataFileAsync("transaction_details.json");
                        var prevJson = System.Text.Encoding.UTF8.GetString(prevBytes);
                        var prev = JsonSerializer.Deserialize<Transaction>(prevJson, _jsonOptions);
                        if (prev != null && prev.CreatedAt != default)
                        {
                            resolvedTransaction.CreatedAt = prev.CreatedAt;
                            break;
                        }
                    }
                    catch (FileNotFoundException) { }
                }
            }
            
            if (resolvedTransaction.CreatedAt == default)
            {
                resolvedTransaction.CreatedAt = DateTime.UtcNow;
            }
        }
        else
        {
            latestVersions = _engine.GetLatestVersionFolders(detailsRoot);
        }
        
        resolvedTransaction.UpdatedAt = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(resolvedTransaction, _jsonOptions);
        var changedFiles = new Dictionary<string, byte[]> { { "transaction_details.json", System.Text.Encoding.UTF8.GetBytes(json) } };

        var resolvedFolders = latestVersions.Select(v => v.FolderName).ToList();

        var metadata = new Dictionary<string, string>
        {
            { "author", author },
            { "device", deviceId },
            { "timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") }
        };

        await _engine.CommitAsync(detailsRoot, deviceId, CommitKind.Res, changedFiles, metadata: metadata, resolvedFolders: resolvedFolders);
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
        await File.WriteAllTextAsync(Path.Combine(tripDir, INITIAL_IMPORT_MARKER), DateTime.UtcNow.ToString("O"));
    }

    public virtual void CompleteInitialImport(string tripSlug)
    {
        var markerPath = Path.Combine(_tripsPath, tripSlug, INITIAL_IMPORT_MARKER);
        if (File.Exists(markerPath)) File.Delete(markerPath);
    }

    public virtual async Task CleanupIncompleteImportsAsync()
    {
        if (!Directory.Exists(_tripsPath)) return;

        var directories = Directory.GetDirectories(_tripsPath);
        foreach (var tripDir in directories)
        {
            var slug = Path.GetFileName(tripDir);
            var markerPath = Path.Combine(tripDir, INITIAL_IMPORT_MARKER);
            if (File.Exists(markerPath))
            {
                await DeleteTripAsync(slug);
            }
        }
    }
}

