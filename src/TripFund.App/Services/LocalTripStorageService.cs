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

public class LocalTripStorageService
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

    public LocalTripStorageService(string? rootPath = null)
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

    private async Task SaveJsonAtomicAsync<T>(string path, T data)
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
        var path = Path.Combine(_rootPath, AppConstants.Files.AppSettings);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
    }
    public virtual Task SaveAppSettingsAsync(AppSettings settings)
    {
        var path = Path.Combine(_rootPath, AppConstants.Files.AppSettings);
        return SaveJsonAtomicAsync(path, settings);
    }

    public virtual async Task<LocalTripRegistry> GetTripRegistryAsync()
    {
        var path = Path.Combine(_rootPath, AppConstants.Files.KnownTrips);
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

    public virtual Task SaveTripRegistryAsync(LocalTripRegistry registry)
    {
        var path = Path.Combine(_rootPath, AppConstants.Files.KnownTrips);
        return SaveJsonAtomicAsync(path, registry);
    }

    public virtual async Task<bool> HasConflictsAsync(string tripSlug)
    {
        var settings = await GetAppSettingsAsync();
        var deviceId = settings?.DeviceId ?? "unknown";
        var author = settings?.AuthorName ?? "Unknown";

        var localTripPath = Path.Combine(_tripsPath, tripSlug);
        
        var configPath = Path.Combine(localTripPath, AppConstants.Folders.Config);
        if (Directory.Exists(configPath) && new VersionedStorageEngine(configPath, deviceId, author).HasConflicts()) return true;

        var transDir = Path.Combine(localTripPath, AppConstants.Folders.Transactions);
        if (Directory.Exists(transDir))
        {
            foreach (var t in Directory.GetDirectories(transDir))
            {
                var detailsRoot = Path.Combine(t, AppConstants.Folders.Details);
                if (Directory.Exists(detailsRoot) && new VersionedStorageEngine(detailsRoot, deviceId, author).HasConflicts()) return true;
            }
        }

        return false;
    }

    public virtual async Task<TripConfig?> GetTripConfigAsync(string tripSlug)
    {
        var settings = await GetAppSettingsAsync();
        var deviceId = settings?.DeviceId ?? "unknown";
        var author = settings?.AuthorName ?? "Unknown";

        var configPath = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Config);
        if (!Directory.Exists(configPath)) return null;

        var engine = new VersionedStorageEngine(configPath, deviceId, author);
        var headPath = engine.ResolveHeadPath();
        if (headPath == null)
        {
            // Initial/Migration scan OR Broken head: if metadata is missing or invalid, recalculate it
            await engine.UpdateHeadAsync();
            headPath = engine.ResolveHeadPath();
        }

        if (headPath != null)
        {
            var leaf = new LocalLeafFolder(headPath);
            if (await leaf.IsDataEmptyAsync()) return null;

            var bytes = await leaf.ReadDataFileAsync(AppConstants.Files.TripConfig);
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<TripConfig>(json, _jsonOptions);
        }

        return null;
    }

    public virtual async Task SaveTripConfigAsync(string tripSlug, TripConfig config, string deviceId, bool isResolve = false)
    {
        var configPath = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Config);
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

        var changedFiles = new Dictionary<string, byte[]> { { AppConstants.Files.TripConfig, bytes } };
        
        CommitKind kind;
        List<string> parentVersions;

        var engine = new VersionedStorageEngine(configPath, deviceId, author);

        if (isResolve)
        {
            kind = CommitKind.Res;
            parentVersions = engine.GetConflictFolderNames();
        }
        else if (engine.IsNew())
        {
            kind = CommitKind.New;
            parentVersions = new List<string>();
        }
        else
        {
            kind = CommitKind.Upd;
            var head = engine.ResolveHeadPath();
            parentVersions = head != null ? new List<string> { Path.GetFileName(head) } : new List<string>();
        }

        var tempRoot = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Temp, AppConstants.Folders.Commits, AppConstants.Folders.Config);
        var folderName = await engine.CommitAsync(kind, changedFiles, parentVersions: parentVersions, contentType: AppConstants.ContentTypes.TripConfig, tempRootPath: tempRoot);
        await RegisterPendingUploadAsync(tripSlug, Path.Combine(AppConstants.Folders.Config, AppConstants.Folders.Versions, folderName));
    }

    public virtual async Task<List<Transaction>> GetTransactionsAsync(string tripSlug)
    {
        var transactionsDir = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Transactions);
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
        var settings = await GetAppSettingsAsync();
        var deviceId = settings?.DeviceId ?? "unknown";
        var author = settings?.AuthorName ?? "Unknown";

        var detailsRoot = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Transactions, transactionId, AppConstants.Folders.Details);
        if (!Directory.Exists(detailsRoot)) return null;

        var engine = new VersionedStorageEngine(detailsRoot, deviceId, author);
        var headPath = engine.ResolveHeadPath();
        if (headPath == null)
        {
            // Initial/Migration scan OR Broken head: if metadata is missing or invalid, recalculate it
            await engine.UpdateHeadAsync();
            headPath = engine.ResolveHeadPath();
        }

        if (headPath != null)
        {
            var leaf = new LocalLeafFolder(headPath);
            var folderName = Path.GetFileName(headPath);
            if (await leaf.IsDataEmptyAsync())
            {
                return new TransactionVersionInfo { VersionFolderName = folderName, IsDeleted = true };
            }

            var bytes = await leaf.ReadDataFileAsync(AppConstants.Files.TransactionDetails);
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            var transaction = JsonSerializer.Deserialize<Transaction>(json, _jsonOptions);
            if (transaction == null) return null;

            return new TransactionVersionInfo
            {
                Transaction = transaction,
                VersionFolderName = folderName
            };
        }

        return null;
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

        var leafPath = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Transactions, transactionId, AppConstants.Folders.Attachments, attachment.Name);
        var path = Path.Combine(leafPath, AppConstants.Folders.Content, attachment.OriginalName);
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
        var settings = await GetAppSettingsAsync();
        var author = settings?.AuthorName ?? "Unknown";

        var transRoot = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Transactions, transaction.Id);
        if (!Directory.Exists(transRoot)) Directory.CreateDirectory(transRoot);

        var detailsRoot = Path.Combine(transRoot, AppConstants.Folders.Details);
        if (!Directory.Exists(detailsRoot)) Directory.CreateDirectory(detailsRoot);

        var engine = new VersionedStorageEngine(detailsRoot, deviceId, author);

        CommitKind kind;
        List<string> parentVersions;

        if (engine.IsNew())
        {
            kind = CommitKind.New;
            parentVersions = new List<string>();
        }
        else
        {
            kind = isDelete ? CommitKind.Del : CommitKind.Upd;
            var head = engine.ResolveHeadPath();
            parentVersions = head != null ? new List<string> { Path.GetFileName(head) } : new List<string>();
        }

        var changedFiles = new Dictionary<string, byte[]>();

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
            changedFiles[AppConstants.Files.TransactionDetails] = System.Text.Encoding.UTF8.GetBytes(json);
            
            // Save attachments to the unversioned 'attachments' folder
            if (attachments != null)
            {
                var attachmentsDir = Path.Combine(transRoot, AppConstants.Folders.Attachments);
                if (!Directory.Exists(attachmentsDir)) Directory.CreateDirectory(attachmentsDir);

                foreach (var attachmentEntry in attachments)
                {
                    var attachmentName = attachmentEntry.Key;
                    var content = attachmentEntry.Value;

                    // Match with Transaction.Attachments to get the OriginalName
                    var attMetadata = transaction.Attachments.FirstOrDefault(a => a.Name == attachmentName);
                    if (attMetadata != null)
                    {
                        var attMetadataDict = new Dictionary<string, string>
                        {
                            { AppConstants.Metadata.Author, author },
                            { AppConstants.Metadata.DeviceId, deviceId },
                            { AppConstants.Metadata.CreatedAt, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                            { AppConstants.Metadata.ContentType, AppConstants.ContentTypes.TransactionAttachment }
                        };

                        var tempAttDir = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Temp, AppConstants.Folders.Commits, AppConstants.Folders.Attachments, transaction.Id, attachmentName);
                        if (Directory.Exists(tempAttDir)) Directory.Delete(tempAttDir, true);
                        Directory.CreateDirectory(tempAttDir);

                        var attLeaf = new LocalLeafFolder(tempAttDir);
                        await attLeaf.WriteDataFileAsync(attMetadata.OriginalName, content);
                        await attLeaf.SaveMetadataAsync(attMetadataDict);

                        var finalAttDir = Path.Combine(attachmentsDir, attachmentName);
                        if (Directory.Exists(finalAttDir)) Directory.Delete(finalAttDir, true);
                        Directory.Move(tempAttDir, finalAttDir);

                        await RegisterPendingUploadAsync(tripSlug, Path.Combine(AppConstants.Folders.Transactions, transaction.Id, AppConstants.Folders.Attachments, attachmentName));
                    }
                }
            }
        }

        var tempRoot = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Temp, AppConstants.Folders.Commits, AppConstants.Folders.Transactions, transaction.Id);
        var folderName = await engine.CommitAsync(kind, changedFiles, parentVersions: parentVersions, contentType: AppConstants.ContentTypes.TransactionDetail, tempRootPath: tempRoot);
        await RegisterPendingUploadAsync(tripSlug, Path.Combine(AppConstants.Folders.Transactions, transaction.Id, AppConstants.Folders.Details, AppConstants.Folders.Versions, folderName));
    }

    public virtual async Task<List<ConflictVersion<Transaction>>> GetConflictingTransactionVersionsAsync(string tripSlug, string transactionId)
    {
        var settings = await GetAppSettingsAsync();
        var deviceId = settings?.DeviceId ?? "unknown";
        var author = settings?.AuthorName ?? "Unknown";

        var detailsRoot = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Transactions, transactionId, AppConstants.Folders.Details);
        var engine = new VersionedStorageEngine(detailsRoot, deviceId, author);
        var latestVersions = engine.GetConflictingFoldersInfo();

        var result = new List<ConflictVersion<Transaction>>();
        foreach (var v in latestVersions)
        {
            var leaf = new LocalLeafFolder(Path.Combine(detailsRoot, AppConstants.Folders.Versions, v.FolderName));
            var metadata = await leaf.GetMetadataAsync();
            
            var cv = new ConflictVersion<Transaction>
            {
                FolderName = v.FolderName,
                DeviceId = v.DeviceId,
                Author = metadata.TryGetValue(AppConstants.Metadata.Author, out var auth) ? auth : "Unknown",
            };

            if (metadata.TryGetValue(AppConstants.Metadata.CreatedAt, out var tsStr) && DateTime.TryParse(tsStr, out var ts))
            {
                cv.Timestamp = ts.ToUniversalTime();
            }

            if (!(await leaf.IsDataEmptyAsync()))
            {
                try 
                {
                    var bytes = await leaf.ReadDataFileAsync(AppConstants.Files.TransactionDetails);
                    var json = System.Text.Encoding.UTF8.GetString(bytes);
                    cv.Data = JsonSerializer.Deserialize<Transaction>(json, _jsonOptions);
                }
                catch (FileNotFoundException) { }
            }
            
            result.Add(cv);
        }
        return result;
    }

    public virtual async Task<List<ConflictVersion<TripConfig>>> GetConflictingConfigVersionsAsync(string tripSlug)
    {
        var settings = await GetAppSettingsAsync();
        var deviceId = settings?.DeviceId ?? "unknown";
        var author = settings?.AuthorName ?? "Unknown";

        var configRoot = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Config);
        var engine = new VersionedStorageEngine(configRoot, deviceId, author);
        var latestVersions = engine.GetConflictingFoldersInfo();

        var result = new List<ConflictVersion<TripConfig>>();
        foreach (var v in latestVersions)
        {
            var leaf = new LocalLeafFolder(Path.Combine(configRoot, AppConstants.Folders.Versions, v.FolderName));
            var metadata = await leaf.GetMetadataAsync();
            
            var cv = new ConflictVersion<TripConfig>
            {
                FolderName = v.FolderName,
                DeviceId = v.DeviceId,
                Author = metadata.TryGetValue(AppConstants.Metadata.Author, out var auth) ? auth : "Unknown",
            };

            if (metadata.TryGetValue(AppConstants.Metadata.CreatedAt, out var tsStr) && DateTime.TryParse(tsStr, out var ts))
            {
                cv.Timestamp = ts.ToUniversalTime();
            }

            if (!(await leaf.IsDataEmptyAsync()))
            {
                try 
                {
                    var bytes = await leaf.ReadDataFileAsync(AppConstants.Files.TripConfig);
                    var json = System.Text.Encoding.UTF8.GetString(bytes);
                    cv.Data = JsonSerializer.Deserialize<TripConfig>(json, _jsonOptions);
                }
                catch (FileNotFoundException) { }
            }
            
            result.Add(cv);
        }
        return result;
    }

    public virtual async Task ResolveConfigConflictAsync(string tripSlug, TripConfig? resolvedConfig, string deviceId)
    {
        var settings = await GetAppSettingsAsync();
        var author = settings?.AuthorName ?? "Unknown";

        var configRoot = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Config);
        var engine = new VersionedStorageEngine(configRoot, deviceId, author);
        var parentVersions = engine.GetConflictFolderNames();

        var changedFiles = new Dictionary<string, byte[]>();
        if (resolvedConfig != null)
        {
            resolvedConfig.Author = author;
            resolvedConfig.UpdatedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(resolvedConfig, _jsonOptions);
            changedFiles[AppConstants.Files.TripConfig] = System.Text.Encoding.UTF8.GetBytes(json);
        }

        var tempRoot = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Temp, AppConstants.Folders.Commits, AppConstants.Folders.Config);
        var folderName = await engine.CommitAsync(CommitKind.Res, changedFiles, parentVersions: parentVersions, contentType: AppConstants.ContentTypes.TripConfig, tempRootPath: tempRoot);
        await RegisterPendingUploadAsync(tripSlug, Path.Combine(AppConstants.Folders.Config, AppConstants.Folders.Versions, folderName));
    }

    public virtual async Task ResolveConflictAsync(string tripSlug, Transaction? resolvedTransaction, string deviceId)
    {
        if (resolvedTransaction == null)
        {
            throw new ArgumentNullException(nameof(resolvedTransaction), "TransactionId is required for deletion resolution.");
        }
        await ResolveConflictAsync(tripSlug, resolvedTransaction.Id, resolvedTransaction, deviceId);
    }

    public virtual async Task ResolveConflictAsync(string tripSlug, string transactionId, Transaction? resolvedTransaction, string deviceId)
    {
        var settings = await GetAppSettingsAsync();
        var author = settings?.AuthorName ?? "Unknown";

        var detailsRoot = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Transactions, transactionId, AppConstants.Folders.Details);
        var engine = new VersionedStorageEngine(detailsRoot, deviceId, author);
        
        var parentVersions = engine.GetConflictFolderNames();

        var changedFiles = new Dictionary<string, byte[]>();
        if (resolvedTransaction != null)
        {
            resolvedTransaction.Author = author;
            if (resolvedTransaction.CreatedAt == default)
            {
                if (parentVersions.Count > 0)
                {
                    var firstParent = parentVersions[0];
                    var leaf = new LocalLeafFolder(Path.Combine(detailsRoot, AppConstants.Folders.Versions, firstParent));
                    try
                    {
                        var prevBytes = await leaf.ReadDataFileAsync(AppConstants.Files.TransactionDetails);
                        var prevJson = System.Text.Encoding.UTF8.GetString(prevBytes);
                        var prev = JsonSerializer.Deserialize<Transaction>(prevJson, _jsonOptions);
                        if (prev != null) resolvedTransaction.CreatedAt = prev.CreatedAt;
                    }
                    catch (FileNotFoundException) { }
                }

                if (resolvedTransaction.CreatedAt == default)
                {
                    var allVersions = engine.GetVersionFolders().OrderByDescending(v => v.Sequence);
                    foreach (var v in allVersions)
                    {
                        var leaf = new LocalLeafFolder(Path.Combine(detailsRoot, AppConstants.Folders.Versions, v.FolderName));
                        try
                        {
                            var prevBytes = await leaf.ReadDataFileAsync(AppConstants.Files.TransactionDetails);
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
            
            resolvedTransaction.UpdatedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(resolvedTransaction, _jsonOptions);
            changedFiles[AppConstants.Files.TransactionDetails] = System.Text.Encoding.UTF8.GetBytes(json);
        }

        var tempRoot = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Temp, AppConstants.Folders.Commits, AppConstants.Folders.Transactions, transactionId);
        var folderName = await engine.CommitAsync(CommitKind.Res, changedFiles, parentVersions: parentVersions, contentType: AppConstants.ContentTypes.TransactionDetail, tempRootPath: tempRoot);
        await RegisterPendingUploadAsync(tripSlug, Path.Combine(AppConstants.Folders.Transactions, transactionId, AppConstants.Folders.Details, AppConstants.Folders.Versions, folderName));
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
            var config = await GetTripConfigAsync(entry.Key);
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

    public virtual async Task<SyncState> GetSyncStateAsync(string tripSlug)
    {
        var path = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Sync, AppConstants.Files.SyncState);
        if (!File.Exists(path)) return new SyncState();

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<SyncState>(json, _jsonOptions) ?? new SyncState();
        }
        catch (JsonException)
        {
            return new SyncState();
        }
    }

    public virtual Task SaveSyncStateAsync(string tripSlug, SyncState state)
    {
        var syncDir = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Sync);
        var path = Path.Combine(syncDir, AppConstants.Files.SyncState);
        return SaveJsonAtomicAsync(path, state);
    }

    public virtual async Task RegisterPendingUploadAsync(string tripSlug, string relativePath)
    {
        var normalizedPath = relativePath.Replace('\\', '/');
        var state = await GetSyncStateAsync(tripSlug);
        
        // Avoid duplicates
        if (state.Sync.Local.Pending.Any(u => u.Path == normalizedPath)) return;

        state.Sync.Local.Pending.Add(new PendingUpload
        {
            Path = normalizedPath,
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture)
        });

        await SaveSyncStateAsync(tripSlug, state);
    }

    public virtual async Task<List<ConflictInfo>> GetConflictsAsync(string tripSlug)
    {
        var conflicts = new List<ConflictInfo>();
        var settings = await GetAppSettingsAsync();
        var deviceId = settings?.DeviceId ?? "unknown";
        var author = settings?.AuthorName ?? "Unknown";

        // 1. Check Trip Config
        var configPath = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Config);
        if (Directory.Exists(configPath) && new VersionedStorageEngine(configPath, deviceId, author).HasConflicts())
        {
            conflicts.Add(new ConflictInfo
            {
                Id = "config",
                Type = "config",
                Label = "Configurazione Viaggio"
            });
        }

        // 2. Check Transactions
        var transDir = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Transactions);
        if (Directory.Exists(transDir))
        {
            var transactionIds = Directory.GetDirectories(transDir).Select(Path.GetFileName);
            
            foreach (var transId in transactionIds)
            {
                if (string.IsNullOrEmpty(transId)) continue;

                var detailsPath = Path.Combine(transDir, transId, AppConstants.Folders.Details);
                if (!Directory.Exists(detailsPath)) continue;

                var engine = new VersionedStorageEngine(detailsPath, deviceId, author);
                if (!engine.HasConflicts()) continue;

                var transVersions = engine.GetConflictingFoldersInfo();
                
                // Use LCA for labeling to show what was there BEFORE the conflict
                var lcaFolderName = engine.GetConflictBaseFolder();
                Transaction? labelTrans = null;
                
                if (lcaFolderName != null)
                {
                    labelTrans = await GetTransactionFromFolderAsync(detailsPath, lcaFolderName);
                }
                
                // Fallback to local branch if LCA is missing or is a deletion
                if (labelTrans == null)
                {
                    labelTrans = await GetLocalBranchTransactionAsync(tripSlug, transId, deviceId);
                }
                
                // Final fallback: try any version that isn't a deletion
                if (labelTrans == null)
                {
                    foreach (var v in transVersions)
                    {
                        labelTrans = await GetTransactionFromFolderAsync(detailsPath, v.FolderName);
                        if (labelTrans != null) break;
                    }
                }

                if (labelTrans != null)
                {
                    if (labelTrans.Type == "expense")
                    {
                        conflicts.Add(new ConflictInfo
                        {
                            Id = transId,
                            Type = "expense",
                            Label = $"Spesa \"{labelTrans.Description}\""
                        });
                    }
                    else if (labelTrans.Type == "contribution")
                    {
                        var currentConfig = await GetTripConfigAsync(tripSlug);
                        var memberSlug = labelTrans.Split.Keys.FirstOrDefault() ?? "unknown";
                        var memberName = (currentConfig != null && currentConfig.Members.TryGetValue(memberSlug, out var m)) ? m.Name : memberSlug;
                        
                        conflicts.Add(new ConflictInfo
                        {
                            Id = transId,
                            Type = "contribution",
                            Label = $"Versamento di {memberName}"
                        });
                    }
                }
                else
                {
                    conflicts.Add(new ConflictInfo
                    {
                        Id = transId,
                        Type = "unknown",
                        Label = $"Conflitto transazione {transId}"
                    });
                }
            }
        }

        return conflicts;
    }

    public virtual async Task UpdateVersionHeadsAfterSyncAsync(string tripSlug, IEnumerable<string> impactedRootPaths)
    {
        var settings = await GetAppSettingsAsync();
        var deviceId = settings?.DeviceId ?? "unknown";
        var author = settings?.AuthorName ?? "Unknown";

        foreach (var rootPath in impactedRootPaths)
        {
            if (!Directory.Exists(rootPath)) continue;
            await new VersionedStorageEngine(rootPath, deviceId, author).UpdateHeadAsync();
        }
    }

    private async Task<Transaction?> GetTransactionFromFolderAsync(string detailsPath, string folderName)
    {
        var leaf = new LocalLeafFolder(Path.Combine(detailsPath, AppConstants.Folders.Versions, folderName));
        try
        {
            if (await leaf.IsDataEmptyAsync()) return null; // Likely a DEL version
            var bytes = await leaf.ReadDataFileAsync(AppConstants.Files.TransactionDetails);
            return JsonSerializer.Deserialize<Transaction>(System.Text.Encoding.UTF8.GetString(bytes), _jsonOptions);
        }
        catch { return null; }
    }

    private async Task<TripConfig?> GetLocalBranchTripConfigAsync(string tripSlug, string deviceId)
    {
        var settings = await GetAppSettingsAsync();
        var author = settings?.AuthorName ?? "Unknown";

        var configPath = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Config);
        if (!Directory.Exists(configPath)) return null;

        var engine = new VersionedStorageEngine(configPath, deviceId, author);
        var allVersions = engine.GetVersionFolders();
        var localLatest = allVersions
            .Where(v => v.DeviceId == deviceId)
            .OrderByDescending(v => v.Sequence)
            .FirstOrDefault();

        if (localLatest == null) return null;

        var leaf = new LocalLeafFolder(Path.Combine(configPath, AppConstants.Folders.Versions, localLatest.FolderName));
        try
        {
            var bytes = await leaf.ReadDataFileAsync(AppConstants.Files.TripConfig);
            return JsonSerializer.Deserialize<TripConfig>(System.Text.Encoding.UTF8.GetString(bytes), _jsonOptions);
        }
        catch { return null; }
    }

    private async Task<Transaction?> GetLocalBranchTransactionAsync(string tripSlug, string transactionId, string deviceId)
    {
        var settings = await GetAppSettingsAsync();
        var author = settings?.AuthorName ?? "Unknown";

        var detailsPath = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Transactions, transactionId, AppConstants.Folders.Details);
        if (!Directory.Exists(detailsPath)) return null;

        var engine = new VersionedStorageEngine(detailsPath, deviceId, author);
        var allVersions = engine.GetVersionFolders();
        var localLatest = allVersions
            .Where(v => v.DeviceId == deviceId)
            .OrderByDescending(v => v.Sequence)
            .FirstOrDefault();

        if (localLatest == null) return null;

        var leaf = new LocalLeafFolder(Path.Combine(detailsPath, AppConstants.Folders.Versions, localLatest.FolderName));
        try
        {
            var bytes = await leaf.ReadDataFileAsync(AppConstants.Files.TransactionDetails);
            return JsonSerializer.Deserialize<Transaction>(System.Text.Encoding.UTF8.GetString(bytes), _jsonOptions);
        }
        catch { return null; }
    }
}
