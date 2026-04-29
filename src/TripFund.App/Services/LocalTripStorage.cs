using System.Text.Json;
using TripFund.App.Models;
using TripFund.App.Constants;

namespace TripFund.App.Services;

public class LocalTripStorage
{
    private readonly LocalStorageService _globalStorage;
    public string TripSlug { get; }

    public LocalTripStorage(LocalStorageService globalStorage, string tripSlug)
    {
        _globalStorage = globalStorage;
        TripSlug = tripSlug;
    }

    public class TransactionVersionInfo
    {
        public Transaction? Transaction { get; set; }
        public string VersionFolderName { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
    }

    public virtual async Task<bool> HasConflictsAsync()
    {
        var settings = await _globalStorage.GetAppSettingsAsync();
        var deviceId = settings?.DeviceId ?? "unknown";
        var author = settings?.AuthorName ?? "Unknown";

        var localTripPath = Path.Combine(_globalStorage.TripsPath, TripSlug);
        
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

    public virtual async Task<TripConfig?> GetTripConfigAsync()
    {
        var settings = await _globalStorage.GetAppSettingsAsync();
        var deviceId = settings?.DeviceId ?? "unknown";
        var author = settings?.AuthorName ?? "Unknown";

        var configPath = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Config);
        if (!Directory.Exists(configPath)) return null;

        var engine = new VersionedStorageEngine(configPath, deviceId, author);
        var headPath = engine.ResolveHeadPath();
        if (headPath == null)
        {
            await engine.UpdateHeadAsync();
            headPath = engine.ResolveHeadPath();
        }

        if (headPath != null)
        {
            var leaf = new LocalLeafFolder(headPath);
            if (await leaf.IsDataEmptyAsync()) return null;

            var bytes = await leaf.ReadDataFileAsync(AppConstants.Files.TripConfig);
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<TripConfig>(json, _globalStorage.JsonOptions);
        }

        return null;
    }

    public virtual async Task SaveTripConfigAsync(TripConfig config, string deviceId, bool isResolve = false)
    {
        var configPath = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Config);
        if (!Directory.Exists(configPath)) Directory.CreateDirectory(configPath);

        config.Id = TripSlug;

        var settings = await _globalStorage.GetAppSettingsAsync();
        var author = settings?.AuthorName ?? "Unknown";
        config.Author = author;

        if (config.CreatedAt == default) config.CreatedAt = DateTime.UtcNow;
        config.UpdatedAt = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(config, _globalStorage.JsonOptions);
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

        var tempRoot = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Temp, AppConstants.Folders.Commits, AppConstants.Folders.Config);
        var folderName = await engine.CommitAsync(kind, changedFiles, parentVersions: parentVersions, contentType: AppConstants.ContentTypes.TripConfig, tempRootPath: tempRoot);
        await RegisterPendingUploadAsync(Path.Combine(AppConstants.Folders.Config, AppConstants.Folders.Versions, folderName));
    }

    public virtual async Task<List<Transaction>> GetTransactionsAsync()
    {
        var transactionsDir = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Transactions);
        if (!Directory.Exists(transactionsDir)) return new List<Transaction>();

        var result = new List<Transaction>();
        foreach (var transRoot in Directory.GetDirectories(transactionsDir))
        {
            var transactionId = Path.GetFileName(transRoot);
            var transaction = await GetLatestTransactionVersionAsync(transactionId);
            if (transaction != null)
            {
                result.Add(transaction);
            }
        }

        return result;
    }

    public virtual async Task<TransactionVersionInfo?> GetLatestTransactionVersionWithDetailsAsync(string transactionId)
    {
        var settings = await _globalStorage.GetAppSettingsAsync();
        var deviceId = settings?.DeviceId ?? "unknown";
        var author = settings?.AuthorName ?? "Unknown";

        var detailsRoot = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Transactions, transactionId, AppConstants.Folders.Details);
        if (!Directory.Exists(detailsRoot)) return null;

        var engine = new VersionedStorageEngine(detailsRoot, deviceId, author);
        var headPath = engine.ResolveHeadPath();
        if (headPath == null)
        {
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
            var transaction = JsonSerializer.Deserialize<Transaction>(json, _globalStorage.JsonOptions);
            if (transaction == null) return null;

            return new TransactionVersionInfo
            {
                Transaction = transaction,
                VersionFolderName = folderName
            };
        }

        return null;
    }

    public virtual async Task<Transaction?> GetLatestTransactionVersionAsync(string transactionId)
    {
        var info = await GetLatestTransactionVersionWithDetailsAsync(transactionId);
        if (info == null || info.IsDeleted) return null;
        return info.Transaction;
    }

    public virtual async Task<string?> GetAttachmentPath(string transactionId, string attachmentName)
    {
        var info = await GetLatestTransactionVersionWithDetailsAsync(transactionId);
        if (info == null || info.IsDeleted || info.Transaction == null) return null;

        var attachment = info.Transaction.Attachments.FirstOrDefault(a => a.Name == attachmentName);
        if (attachment == null) return null;

        var leafPath = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Transactions, transactionId, AppConstants.Folders.Attachments, attachment.Name);
        var path = Path.Combine(leafPath, AppConstants.Folders.Content, attachment.OriginalName);
        return File.Exists(path) ? path : null;
    }

    public virtual async Task SaveTransactionAsync(Transaction transaction, string deviceId, bool isDelete = false, Dictionary<string, byte[]>? attachments = null)
    {
        var settings = await _globalStorage.GetAppSettingsAsync();
        var author = settings?.AuthorName ?? "Unknown";

        var transRoot = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Transactions, transaction.Id);
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
                var existing = await GetLatestTransactionVersionAsync(transaction.Id);
                if (existing != null) transaction.CreatedAt = existing.CreatedAt;
                else transaction.CreatedAt = DateTime.UtcNow;
            }
            transaction.UpdatedAt = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(transaction, _globalStorage.JsonOptions);
            changedFiles[AppConstants.Files.TransactionDetails] = System.Text.Encoding.UTF8.GetBytes(json);
            
            if (attachments != null)
            {
                var attachmentsDir = Path.Combine(transRoot, AppConstants.Folders.Attachments);
                if (!Directory.Exists(attachmentsDir)) Directory.CreateDirectory(attachmentsDir);

                foreach (var attachmentEntry in attachments)
                {
                    var attachmentName = attachmentEntry.Key;
                    var content = attachmentEntry.Value;

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

                        var tempAttDir = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Temp, AppConstants.Folders.Commits, AppConstants.Folders.Attachments, transaction.Id, attachmentName);
                        if (Directory.Exists(tempAttDir)) Directory.Delete(tempAttDir, true);
                        Directory.CreateDirectory(tempAttDir);

                        var attLeaf = new LocalLeafFolder(tempAttDir);
                        await attLeaf.WriteDataFileAsync(attMetadata.OriginalName, content);
                        await attLeaf.SaveMetadataAsync(attMetadataDict);

                        var finalAttDir = Path.Combine(attachmentsDir, attachmentName);
                        if (Directory.Exists(finalAttDir)) Directory.Delete(finalAttDir, true);
                        Directory.Move(tempAttDir, finalAttDir);

                        await RegisterPendingUploadAsync(Path.Combine(AppConstants.Folders.Transactions, transaction.Id, AppConstants.Folders.Attachments, attachmentName));
                    }
                }
            }
        }

        var tempRoot = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Temp, AppConstants.Folders.Commits, AppConstants.Folders.Transactions, transaction.Id);
        var folderName = await engine.CommitAsync(kind, changedFiles, parentVersions: parentVersions, contentType: AppConstants.ContentTypes.TransactionDetail, tempRootPath: tempRoot);
        await RegisterPendingUploadAsync(Path.Combine(AppConstants.Folders.Transactions, transaction.Id, AppConstants.Folders.Details, AppConstants.Folders.Versions, folderName));
    }

    public virtual async Task<List<ConflictVersion<Transaction>>> GetConflictingTransactionVersionsAsync(string transactionId)
    {
        var settings = await _globalStorage.GetAppSettingsAsync();
        var deviceId = settings?.DeviceId ?? "unknown";
        var author = settings?.AuthorName ?? "Unknown";

        var detailsRoot = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Transactions, transactionId, AppConstants.Folders.Details);
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
                    cv.Data = JsonSerializer.Deserialize<Transaction>(json, _globalStorage.JsonOptions);
                }
                catch (FileNotFoundException) { }
            }
            
            result.Add(cv);
        }
        return result;
    }

    public virtual async Task<List<ConflictVersion<TripConfig>>> GetConflictingConfigVersionsAsync()
    {
        var settings = await _globalStorage.GetAppSettingsAsync();
        var deviceId = settings?.DeviceId ?? "unknown";
        var author = settings?.AuthorName ?? "Unknown";

        var configRoot = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Config);
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
                    cv.Data = JsonSerializer.Deserialize<TripConfig>(json, _globalStorage.JsonOptions);
                }
                catch (FileNotFoundException) { }
            }
            
            result.Add(cv);
        }
        return result;
    }

    public virtual async Task ResolveConfigConflictAsync(TripConfig? resolvedConfig, string deviceId)
    {
        var settings = await _globalStorage.GetAppSettingsAsync();
        var author = settings?.AuthorName ?? "Unknown";

        var configRoot = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Config);
        var engine = new VersionedStorageEngine(configRoot, deviceId, author);
        var parentVersions = engine.GetConflictFolderNames();

        var changedFiles = new Dictionary<string, byte[]>();
        if (resolvedConfig != null)
        {
            resolvedConfig.Author = author;
            resolvedConfig.UpdatedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(resolvedConfig, _globalStorage.JsonOptions);
            changedFiles[AppConstants.Files.TripConfig] = System.Text.Encoding.UTF8.GetBytes(json);
        }

        var tempRoot = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Temp, AppConstants.Folders.Commits, AppConstants.Folders.Config);
        var folderName = await engine.CommitAsync(CommitKind.Res, changedFiles, parentVersions: parentVersions, contentType: AppConstants.ContentTypes.TripConfig, tempRootPath: tempRoot);
        await RegisterPendingUploadAsync(Path.Combine(AppConstants.Folders.Config, AppConstants.Folders.Versions, folderName));
    }

    public virtual async Task ResolveConflictAsync(Transaction? resolvedTransaction, string deviceId)
    {
        if (resolvedTransaction == null)
        {
            throw new ArgumentNullException(nameof(resolvedTransaction), "TransactionId is required for deletion resolution.");
        }
        await ResolveConflictAsync(resolvedTransaction.Id, resolvedTransaction, deviceId);
    }

    public virtual async Task ResolveConflictAsync(string transactionId, Transaction? resolvedTransaction, string deviceId)
    {
        var settings = await _globalStorage.GetAppSettingsAsync();
        var author = settings?.AuthorName ?? "Unknown";

        var detailsRoot = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Transactions, transactionId, AppConstants.Folders.Details);
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
                        var prev = JsonSerializer.Deserialize<Transaction>(prevJson, _globalStorage.JsonOptions);
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
                            var prev = JsonSerializer.Deserialize<Transaction>(prevJson, _globalStorage.JsonOptions);
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
            var json = JsonSerializer.Serialize(resolvedTransaction, _globalStorage.JsonOptions);
            changedFiles[AppConstants.Files.TransactionDetails] = System.Text.Encoding.UTF8.GetBytes(json);
        }

        var tempRoot = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Temp, AppConstants.Folders.Commits, AppConstants.Folders.Transactions, transactionId);
        var folderName = await engine.CommitAsync(CommitKind.Res, changedFiles, parentVersions: parentVersions, contentType: AppConstants.ContentTypes.TransactionDetail, tempRootPath: tempRoot);
        await RegisterPendingUploadAsync(Path.Combine(AppConstants.Folders.Transactions, transactionId, AppConstants.Folders.Details, AppConstants.Folders.Versions, folderName));
    }

    public virtual async Task<SyncState> GetSyncStateAsync()
    {
        var path = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Sync, AppConstants.Files.SyncState);
        if (!File.Exists(path)) return new SyncState();

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<SyncState>(json, _globalStorage.JsonOptions) ?? new SyncState();
        }
        catch (JsonException)
        {
            return new SyncState();
        }
    }

    public virtual Task SaveSyncStateAsync(SyncState state)
    {
        var syncDir = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Sync);
        var path = Path.Combine(syncDir, AppConstants.Files.SyncState);
        return _globalStorage.SaveJsonAtomicAsync(path, state);
    }

    public virtual async Task RegisterPendingUploadAsync(string relativePath)
    {
        var normalizedPath = relativePath.Replace('\\', '/');
        var state = await GetSyncStateAsync();
        
        if (state.Sync.Local.Pending.Any(u => u.Path == normalizedPath)) return;

        state.Sync.Local.Pending.Add(new PendingUpload
        {
            Path = normalizedPath,
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture)
        });

        await SaveSyncStateAsync(state);
    }

    public virtual async Task<List<ConflictInfo>> GetConflictsAsync()
    {
        var conflicts = new List<ConflictInfo>();
        var settings = await _globalStorage.GetAppSettingsAsync();
        var deviceId = settings?.DeviceId ?? "unknown";
        var author = settings?.AuthorName ?? "Unknown";

        var configPath = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Config);
        if (Directory.Exists(configPath) && new VersionedStorageEngine(configPath, deviceId, author).HasConflicts())
        {
            conflicts.Add(new ConflictInfo
            {
                Id = "config",
                Type = "config",
                Label = "Configurazione Viaggio"
            });
        }

        var transDir = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Transactions);
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
                
                var lcaFolderName = engine.GetConflictBaseFolder();
                Transaction? labelTrans = null;
                
                if (lcaFolderName != null)
                {
                    labelTrans = await GetTransactionFromFolderAsync(detailsPath, lcaFolderName);
                }
                
                if (labelTrans == null)
                {
                    labelTrans = await GetLocalBranchTransactionAsync(transId, deviceId);
                }
                
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
                        var currentConfig = await GetTripConfigAsync();
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

    public virtual async Task UpdateVersionHeadsAfterSyncAsync(IEnumerable<string> impactedRootPaths)
    {
        var settings = await _globalStorage.GetAppSettingsAsync();
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
            if (await leaf.IsDataEmptyAsync()) return null;
            var bytes = await leaf.ReadDataFileAsync(AppConstants.Files.TransactionDetails);
            return JsonSerializer.Deserialize<Transaction>(System.Text.Encoding.UTF8.GetString(bytes), _globalStorage.JsonOptions);
        }
        catch { return null; }
    }

    private async Task<Transaction?> GetLocalBranchTransactionAsync(string transactionId, string deviceId)
    {
        var settings = await _globalStorage.GetAppSettingsAsync();
        var author = settings?.AuthorName ?? "Unknown";

        var detailsPath = Path.Combine(_globalStorage.TripsPath, TripSlug, AppConstants.Folders.Transactions, transactionId, AppConstants.Folders.Details);
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
            return JsonSerializer.Deserialize<Transaction>(System.Text.Encoding.UTF8.GetString(bytes), _globalStorage.JsonOptions);
        }
        catch { return null; }
    }
}
