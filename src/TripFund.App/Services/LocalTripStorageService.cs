using System.Text.Json;
using System.Text.Json.Serialization;
using TripFund.App.Models;
using TripFund.App.Utilities;

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
    private const string INITIAL_IMPORT_MARKER = ".initial_import";
    private readonly VersionedStorageEngine _engine = new();
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

    public virtual Task<bool> HasConflictsAsync(string tripSlug)
    {
        var localTripPath = Path.Combine(_tripsPath, tripSlug);
        
        var configPath = Path.Combine(localTripPath, "config_versioned");
        if (Directory.Exists(configPath))
        {
            var latest = _engine.GetLatestVersionFolders(configPath);
            if (latest.Count > 1) return Task.FromResult(true);
        }

        var transDir = Path.Combine(localTripPath, "transactions");
        if (Directory.Exists(transDir))
        {
            foreach (var t in Directory.GetDirectories(transDir))
            {
                var detailsRoot = Path.Combine(t, "details_versioned");
                if (Directory.Exists(detailsRoot))
                {
                    var latest = _engine.GetLatestVersionFolders(detailsRoot);
                    if (latest.Count > 1) return Task.FromResult(true);
                }
            }
        }

        return Task.FromResult(false);
    }

    public virtual async Task<TripConfig?> GetTripConfigAsync(string tripSlug)
    {
        var configPath = Path.Combine(_tripsPath, tripSlug, "config_versioned");
        if (!Directory.Exists(configPath)) return null;

        var latestVersions = _engine.GetLatestVersionFolders(configPath);
        if (latestVersions.Count == 0) return null;

        if (latestVersions.Count > 1)
        {
            var settings = await GetAppSettingsAsync();
            var local = await GetLocalBranchTripConfigAsync(tripSlug, settings?.DeviceId ?? "unknown");
            if (local != null) return local;
        }

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
            { "createdAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") }
        };

        var folderName = await _engine.CommitAsync(configPath, deviceId, kind, changedFiles, metadata: metadata, contentType: "tripfund/trip-config");
        await RegisterPendingUploadAsync(tripSlug, Path.Combine("config_versioned", folderName));
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
            var settings = await GetAppSettingsAsync();
            var local = await GetLocalBranchTransactionAsync(tripSlug, transactionId, settings?.DeviceId ?? "unknown");
            if (local != null)
            {
                return new TransactionVersionInfo
                {
                    Transaction = local,
                    VersionFolderName = "conflict-local-fallback" // Custom folder name to indicate local fallback
                };
            }
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
            { "createdAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") }
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
                        var attMetadataDict = new Dictionary<string, string>
                        {
                            { "author", author },
                            { "device", deviceId },
                            { "createdAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                            { "contentType", "tripfund/transaction-attachment" }
                        };

                        var leafDir = Path.Combine(attachmentsDir, attachmentName);
                        var attLeaf = new LocalLeafFolder(leafDir);
                        await attLeaf.WriteDataFileAsync(attMetadata.OriginalName, content);
                        await attLeaf.SaveMetadataAsync(attMetadataDict);
                        await attLeaf.WriteMarkerAsync(".active");
                        await RegisterPendingUploadAsync(tripSlug, Path.Combine("transactions", transaction.Id, "attachments", attachmentName));
                    }
                }
            }
        }

        var folderName = await _engine.CommitAsync(detailsRoot, deviceId, kind, changedFiles, metadata: metadata, contentType: "tripfund/transaction-detail");
        await RegisterPendingUploadAsync(tripSlug, Path.Combine("transactions", transaction.Id, "details_versioned", folderName));
    }

    public virtual async Task<List<ConflictVersion<Transaction>>> GetConflictingTransactionVersionsAsync(string tripSlug, string transactionId)
    {
        var detailsRoot = Path.Combine(_tripsPath, tripSlug, "transactions", transactionId, "details_versioned");
        var latestVersions = _engine.GetLatestVersionFolders(detailsRoot);

        var result = new List<ConflictVersion<Transaction>>();
        foreach (var v in latestVersions)
        {
            var leaf = new LocalLeafFolder(Path.Combine(detailsRoot, v.FolderName));
            var metadata = await leaf.GetMetadataAsync();
            
            var cv = new ConflictVersion<Transaction>
            {
                FolderName = v.FolderName,
                DeviceId = v.DeviceId,
                Author = metadata.TryGetValue("author", out var author) ? author : "Unknown",
            };

            if (metadata.TryGetValue("createdAt", out var tsStr) && DateTime.TryParse(tsStr, out var ts))
            {
                cv.Timestamp = ts.ToUniversalTime();
            }

            if (!(await leaf.IsDataEmptyAsync()))
            {
                try 
                {
                    var bytes = await leaf.ReadDataFileAsync("transaction_details.json");
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
        var configRoot = Path.Combine(_tripsPath, tripSlug, "config_versioned");
        var latestVersions = _engine.GetLatestVersionFolders(configRoot);

        var result = new List<ConflictVersion<TripConfig>>();
        foreach (var v in latestVersions)
        {
            var leaf = new LocalLeafFolder(Path.Combine(configRoot, v.FolderName));
            var metadata = await leaf.GetMetadataAsync();
            
            var cv = new ConflictVersion<TripConfig>
            {
                FolderName = v.FolderName,
                DeviceId = v.DeviceId,
                Author = metadata.TryGetValue("author", out var author) ? author : "Unknown",
            };

            if (metadata.TryGetValue("createdAt", out var tsStr) && DateTime.TryParse(tsStr, out var ts))
            {
                cv.Timestamp = ts.ToUniversalTime();
            }

            if (!(await leaf.IsDataEmptyAsync()))
            {
                try 
                {
                    var bytes = await leaf.ReadDataFileAsync("trip_config.json");
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
        var configRoot = Path.Combine(_tripsPath, tripSlug, "config_versioned");
        var latestVersions = _engine.GetLatestVersionFolders(configRoot);

        var settings = await GetAppSettingsAsync();
        var author = settings?.AuthorName ?? "Unknown";

        var changedFiles = new Dictionary<string, byte[]>();
        if (resolvedConfig != null)
        {
            resolvedConfig.Author = author;
            resolvedConfig.UpdatedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(resolvedConfig, _jsonOptions);
            changedFiles["trip_config.json"] = System.Text.Encoding.UTF8.GetBytes(json);
        }

        var resolvedFolders = latestVersions.Select(v => v.FolderName).ToList();

        var metadata = new Dictionary<string, string>
        {
            { "author", author },
            { "device", deviceId },
            { "createdAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") }
        };

        var folderName = await _engine.CommitAsync(configRoot, deviceId, CommitKind.Res, changedFiles, metadata: metadata, resolvedFolders: resolvedFolders, contentType: "tripfund/trip-config");
        await RegisterPendingUploadAsync(tripSlug, Path.Combine("config_versioned", folderName));
    }

    public virtual async Task ResolveConflictAsync(string tripSlug, Transaction? resolvedTransaction, string deviceId)
    {
        if (resolvedTransaction == null)
        {
            // We need to know which transactionId we are resolving.
            // This is a bit tricky if we only have null. 
            // But ResolveConflictAsync is called from the modal which knows the transactionId from the ConflictInfo.
            // I will add an overload or change signature to include transactionId.
            throw new ArgumentNullException(nameof(resolvedTransaction), "TransactionId is required for deletion resolution.");
        }
        await ResolveConflictAsync(tripSlug, resolvedTransaction.Id, resolvedTransaction, deviceId);
    }

    public virtual async Task ResolveConflictAsync(string tripSlug, string transactionId, Transaction? resolvedTransaction, string deviceId)
    {
        var detailsRoot = Path.Combine(_tripsPath, tripSlug, "transactions", transactionId, "details_versioned");
        
        var settings = await GetAppSettingsAsync();
        var author = settings?.AuthorName ?? "Unknown";
        
        IReadOnlyList<VersionFolderInfo> latestVersions = _engine.GetLatestVersionFolders(detailsRoot);

        var changedFiles = new Dictionary<string, byte[]>();
        if (resolvedTransaction != null)
        {
            resolvedTransaction.Author = author;
            if (resolvedTransaction.CreatedAt == default)
            {
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
            
            resolvedTransaction.UpdatedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(resolvedTransaction, _jsonOptions);
            changedFiles["transaction_details.json"] = System.Text.Encoding.UTF8.GetBytes(json);
        }

        var resolvedFolders = latestVersions.Select(v => v.FolderName).ToList();

        var metadata = new Dictionary<string, string>
        {
            { "author", author },
            { "device", deviceId },
            { "createdAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") }
        };

        var folderName = await _engine.CommitAsync(detailsRoot, deviceId, CommitKind.Res, changedFiles, metadata: metadata, resolvedFolders: resolvedFolders, contentType: "tripfund/transaction-detail");
        await RegisterPendingUploadAsync(tripSlug, Path.Combine("transactions", transactionId, "details_versioned", folderName));
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

    public virtual async Task<SyncState> GetSyncStateAsync(string tripSlug)
    {
        var path = Path.Combine(_tripsPath, tripSlug, "sync_state.json");
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

    public virtual async Task SaveSyncStateAsync(string tripSlug, SyncState state)
    {
        var path = Path.Combine(_tripsPath, tripSlug, "sync_state.json");
        var tempPath = path + ".tmp";

        var json = JsonSerializer.Serialize(state, _jsonOptions);
        await File.WriteAllTextAsync(tempPath, json);

        if (File.Exists(path)) File.Delete(path);
        File.Move(tempPath, path);
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

        // 1. Check Trip Config
        var configPath = Path.Combine(_tripsPath, tripSlug, "config_versioned");
        if (Directory.Exists(configPath))
        {
            var configVersions = _engine.GetLatestVersionFolders(configPath);
            if (configVersions.Count > 1)
            {
                conflicts.Add(new ConflictInfo
                {
                    Id = "config",
                    Type = "config",
                    Label = "Configurazione Viaggio"
                });
            }
        }

        // 2. Check Transactions
        var transDir = Path.Combine(_tripsPath, tripSlug, "transactions");
        if (Directory.Exists(transDir))
        {
            var transactionIds = Directory.GetDirectories(transDir).Select(Path.GetFileName);
            
            foreach (var transId in transactionIds)
            {
                if (string.IsNullOrEmpty(transId)) continue;

                var detailsPath = Path.Combine(transDir, transId, "details_versioned");
                if (!Directory.Exists(detailsPath)) continue;

                var transVersions = _engine.GetLatestVersionFolders(detailsPath);
                if (transVersions.Count > 1)
                {
                    // Use LCA for labeling to show what was there BEFORE the conflict
                    var lcaFolderName = _engine.GetBaseVersionFolder(detailsPath, transVersions);
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
        }

        return conflicts;
    }

    private async Task<Transaction?> GetTransactionFromFolderAsync(string detailsPath, string folderName)
    {
        var leaf = new LocalLeafFolder(Path.Combine(detailsPath, folderName));
        try
        {
            if (await leaf.IsDataEmptyAsync()) return null; // Likely a DEL version
            var bytes = await leaf.ReadDataFileAsync("transaction_details.json");
            return JsonSerializer.Deserialize<Transaction>(System.Text.Encoding.UTF8.GetString(bytes), _jsonOptions);
        }
        catch { return null; }
    }

    private async Task<TripConfig?> GetTripConfigFromFolderAsync(string configPath, string folderName)
    {
        var leaf = new LocalLeafFolder(Path.Combine(configPath, folderName));
        try
        {
            if (await leaf.IsDataEmptyAsync()) return null;
            var bytes = await leaf.ReadDataFileAsync("trip_config.json");
            return JsonSerializer.Deserialize<TripConfig>(System.Text.Encoding.UTF8.GetString(bytes), _jsonOptions);
        }
        catch { return null; }
    }

    private async Task<TripConfig?> GetLocalBranchTripConfigAsync(string tripSlug, string deviceId)
    {
        var configPath = Path.Combine(_tripsPath, tripSlug, "config_versioned");
        if (!Directory.Exists(configPath)) return null;

        var allVersions = _engine.GetVersionFolders(configPath);
        var localLatest = allVersions
            .Where(v => v.DeviceId == deviceId)
            .OrderByDescending(v => v.Sequence)
            .FirstOrDefault();

        if (localLatest == null) return null;

        var leaf = new LocalLeafFolder(Path.Combine(configPath, localLatest.FolderName));
        try
        {
            var bytes = await leaf.ReadDataFileAsync("trip_config.json");
            return JsonSerializer.Deserialize<TripConfig>(System.Text.Encoding.UTF8.GetString(bytes), _jsonOptions);
        }
        catch { return null; }
    }

    private async Task<Transaction?> GetLocalBranchTransactionAsync(string tripSlug, string transactionId, string deviceId)
    {
        var detailsPath = Path.Combine(_tripsPath, tripSlug, "transactions", transactionId, "details_versioned");
        if (!Directory.Exists(detailsPath)) return null;

        var allVersions = _engine.GetVersionFolders(detailsPath);
        var localLatest = allVersions
            .Where(v => v.DeviceId == deviceId)
            .OrderByDescending(v => v.Sequence)
            .FirstOrDefault();

        if (localLatest == null) return null;

        var leaf = new LocalLeafFolder(Path.Combine(detailsPath, localLatest.FolderName));
        try
        {
            var bytes = await leaf.ReadDataFileAsync("transaction_details.json");
            return JsonSerializer.Deserialize<Transaction>(System.Text.Encoding.UTF8.GetString(bytes), _jsonOptions);
        }
        catch { return null; }
    }
}

