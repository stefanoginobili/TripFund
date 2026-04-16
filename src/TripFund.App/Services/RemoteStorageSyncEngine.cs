using System.Net.Http.Headers;
using TripFund.App.Models;

namespace TripFund.App.Services;

public class RemoteStorageSyncEngine
{
    private readonly LocalTripStorageService _localStorage;
    private readonly VersionedStorageEngine _engine = new();

    public RemoteStorageSyncEngine(LocalTripStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    public async Task SynchronizeAsync(string tripSlug, IRemoteFileSystem fileSystem, Action<string, bool>? onSyncStateChanged = null)
    {
        var logger = new RemoteStorageLogger();
        fileSystem.Logger = logger;
        logger.LogInfo($"Starting synchronization for trip: {tripSlug}");

        onSyncStateChanged?.Invoke(tripSlug, true);
        try
        {
            var registry = await _localStorage.GetTripRegistryAsync();
            if (!registry.Trips.TryGetValue(tripSlug, out var entry) || entry.RemoteStorage == null)
            {
                logger.LogInfo("Trip not found in registry or remote storage not configured.");
                return;
            }

            if (!entry.RemoteStorage.Parameters.TryGetValue("folderId", out var folderId))
            {
                logger.LogError("Remote folderId not found in parameters.");
                return;
            }

            logger.LogInfo("Authenticating with remote storage provider...");
            await fileSystem.EnsureAuthenticatedAsync(entry.RemoteStorage.Parameters);
            // After authentication, check if registry needs to be saved (e.g., updated refresh token)
            await _localStorage.SaveTripRegistryAsync(registry);

            // Check if root is readonly
            logger.LogInfo("Checking write permissions on remote storage...");
            var canWrite = await CheckFolderWritePermissionAsync(folderId, entry.RemoteStorage.Parameters, fileSystem);
            if (entry.RemoteStorage.Readonly != !canWrite)
            {
                entry.RemoteStorage.Readonly = !canWrite;
                await _localStorage.SaveTripRegistryAsync(registry);
            }
            logger.LogInfo($"Remote storage is {(entry.RemoteStorage.Readonly ? "READ-ONLY" : "READ-WRITE")}");

            // 1. Download Phase
            logger.LogInfo("Starting DOWNLOAD phase...");
            var localTripPath = Path.Combine(_localStorage.TripsPath, tripSlug);
            await SyncDownAsync(folderId, localTripPath, entry.RemoteStorage.Parameters, fileSystem);

            // 2. Integrity & Conflict Check
            logger.LogInfo("Checking for local conflicts...");
            var conflicts = new List<VersionedFolderConflictException>();

            var configPath = Path.Combine(localTripPath, "config_versioned");
            if (Directory.Exists(configPath))
            {
                var latest = _engine.GetLatestVersionFolders(configPath);
                if (latest.Count > 1)
                {
                    var diverging = latest.Select(v => v.FolderName).ToList();
                    var baseVer = _engine.GetBaseVersionFolder(configPath, latest[0].Sequence);
                    conflicts.Add(new TripConfigConflictException(diverging, baseVer));
                }
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
                        if (latest.Count > 1)
                        {
                            var diverging = latest.Select(v => v.FolderName).ToList();
                            var baseVer = _engine.GetBaseVersionFolder(detailsRoot, latest[0].Sequence);
                            var transactionId = Path.GetFileName(t);
                            conflicts.Add(new TransactionConflictException(transactionId, diverging, baseVer));
                        }
                    }
                }
            }

            entry.RemoteStorage.HasConflicts = conflicts.Count > 0;
            await _localStorage.SaveTripRegistryAsync(registry);

            if (conflicts.Count > 0)
            {
                logger.LogInfo($"Conflict detected ({conflicts.Count}). Sync aborted. Resolution required.");
                throw new SyncConflictException(conflicts);
            }

            // 3. Upload Phase
            if (!entry.RemoteStorage.Readonly)
            {
                logger.LogInfo("Starting UPLOAD phase...");
                await SyncUpAsync(localTripPath, folderId, entry.RemoteStorage.Parameters, fileSystem);
            }
            else
            {
                logger.LogInfo("Skipping UPLOAD phase (Read-Only).");
            }
            
            logger.LogInfo("Synchronization completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError("An error occurred during synchronization", ex);
            throw;
        }
        finally
        {
            onSyncStateChanged?.Invoke(tripSlug, false);
            
            // Save the log locally (STAYS LOCAL, NEVER UPLOADED)
            try
            {
                await SaveSyncLogLocallyAsync(tripSlug, fileSystem);
            }
            catch (Exception logEx)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save sync log locally: {logEx.Message}");
            }
        }
    }

    private async Task SaveSyncLogLocallyAsync(string tripSlug, IRemoteFileSystem fileSystem)
    {
        if (fileSystem.Logger == null) return;

        var logContent = fileSystem.Logger.GetLogContent();

        // The requirement is debug/sync/[TripSlug].txt as a sibling of "trips"
        var debugPath = Path.Combine(_localStorage.AppDataPath, "debug", "sync");
        if (!Directory.Exists(debugPath))
        {
            Directory.CreateDirectory(debugPath);
        }

        var logFile = Path.Combine(debugPath, $"{tripSlug}.txt");
        await File.WriteAllTextAsync(logFile, logContent);
    }

    private async Task<RemoteItem?> GetOrCreateFolderAsync(string parentId, string name, Dictionary<string, string> parameters, IRemoteFileSystem fileSystem)
    {
        var children = await fileSystem.ListChildrenAsync(parentId, parameters);
        var existing = children.FirstOrDefault(c => c.Name == name && c.IsFolder);
        if (existing != null) return existing;

        return await fileSystem.CreateFolderAsync(parentId, name, parameters);
    }

    private async Task<bool> CheckFolderWritePermissionAsync(string folderId, Dictionary<string, string> parameters, IRemoteFileSystem fileSystem)
    {
        try
        {
            var settings = await _localStorage.GetAppSettingsAsync();
            var deviceId = settings?.DeviceId ?? "unknown-device";
            var fileName = $".rw-test-{deviceId}";

            var children = await fileSystem.ListChildrenAsync(folderId, parameters);
            var existing = children.FirstOrDefault(c => c.Name == fileName);
            if (existing != null)
            {
                try { await fileSystem.DeleteFileAsync(existing.Id, parameters); return true; } catch { }
            }

            var uploaded = await fileSystem.UploadFileAsync(folderId, fileName, new byte[] { 0x01 }, parameters);
            if (uploaded == null) return false;

            await fileSystem.DeleteFileAsync(uploaded.Id, parameters);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task SyncDownAsync(string remoteFolderId, string localPath, Dictionary<string, string> parameters, IRemoteFileSystem fileSystem)
    {
        if (File.Exists(Path.Combine(localPath, ".synched"))) return;

        if (fileSystem.Logger != null) fileSystem.Logger.CurrentFolderName = Path.GetFileName(localPath);
        var children = await fileSystem.ListChildrenAsync(remoteFolderId, parameters);
        if (children.Count == 0) return;

        bool isLeaf = children.Any(c => c.Name == ".metadata" || c.Name == ".data");

        if (isLeaf)
        {
            var localLeaf = new LocalLeafFolder(localPath);
            var remoteLeaf = new RemoteLeafFolder(fileSystem, remoteFolderId, parameters);

            bool isFullyCopiedLocally = false;
            if (Directory.Exists(localPath))
            {
                bool hasDownloading = await localLeaf.HasMarkerAsync(".downloading");
                bool hasMetadata = File.Exists(Path.Combine(localPath, ".metadata"));
                if (hasMetadata && !hasDownloading)
                {
                    isFullyCopiedLocally = true;
                }
            }

            if (!isFullyCopiedLocally)
            {
                if (Directory.Exists(localPath))
                {
                    foreach (var file in Directory.GetFiles(localPath)) File.Delete(file);
                    foreach (var dir in Directory.GetDirectories(localPath)) Directory.Delete(dir, true);
                }
                else
                {
                    Directory.CreateDirectory(localPath);
                }

                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                await localLeaf.WriteMarkerAsync(".downloading", $"begin={timestamp}");

                // Copy Metadata
                var metadata = await remoteLeaf.GetMetadataAsync();
                await localLeaf.SaveMetadataAsync(metadata);

                // Copy Data
                await localLeaf.EnsureDataDirectoryAsync();
                var dataFiles = await remoteLeaf.ListDataFilesAsync();
                foreach (var fileName in dataFiles)
                {
                    var content = await remoteLeaf.ReadDataFileAsync(fileName);
                    await localLeaf.WriteDataFileAsync(fileName, content);
                }

                await localLeaf.DeleteMarkerAsync(".downloading");
            }

            await localLeaf.WriteMarkerAsync(".synched");
        }
        else
        {
            // Node folder
            foreach (var child in children.Where(c => c.IsFolder && !VersionedStorageEngine.IgnoredSystemFiles.Any(r => r.IsMatch(c.Name))))
            {
                var localChildPath = Path.Combine(localPath, child.Name);
                if (File.Exists(Path.Combine(localChildPath, ".synched"))) continue;

                if (!Directory.Exists(localChildPath)) Directory.CreateDirectory(localChildPath);
                await SyncDownAsync(child.Id, localChildPath, parameters, fileSystem);
            }
        }
    }

    private async Task SyncUpAsync(string localPath, string remoteFolderId, Dictionary<string, string> parameters, IRemoteFileSystem fileSystem)
    {
        if (File.Exists(Path.Combine(localPath, ".synched"))) return;

        if (fileSystem.Logger != null) fileSystem.Logger.CurrentFolderName = Path.GetFileName(localPath);

        bool isLeaf = Directory.Exists(Path.Combine(localPath, ".data")) || File.Exists(Path.Combine(localPath, ".metadata"));

        if (isLeaf)
        {
            var localLeaf = new LocalLeafFolder(localPath);
            var remoteLeaf = new RemoteLeafFolder(fileSystem, remoteFolderId, parameters);

            var remoteChildren = await fileSystem.ListChildrenAsync(remoteFolderId, parameters);
            bool isFullyCopiedRemotely = false;
            bool hasUploading = remoteChildren.Any(c => c.Name == ".uploading");
            bool hasMetadata = remoteChildren.Any(c => c.Name == ".metadata");

            if (hasMetadata && !hasUploading)
            {
                isFullyCopiedRemotely = true;
            }

            if (!isFullyCopiedRemotely)
            {
                foreach (var child in remoteChildren)
                {
                    await fileSystem.DeleteFileAsync(child.Id, parameters);
                }

                var settings = await _localStorage.GetAppSettingsAsync();
                var deviceId = settings?.DeviceId ?? "unknown";
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                await remoteLeaf.WriteMarkerAsync(".uploading", $"source={deviceId}\nbegin={timestamp}");

                // Copy Metadata
                var metadata = await localLeaf.GetMetadataAsync();
                await remoteLeaf.SaveMetadataAsync(metadata);

                // Copy Data
                await remoteLeaf.EnsureDataDirectoryAsync();
                var dataFiles = await localLeaf.ListDataFilesAsync();
                foreach (var fileName in dataFiles)
                {
                    var content = await localLeaf.ReadDataFileAsync(fileName);
                    await remoteLeaf.WriteDataFileAsync(fileName, content);
                }

                await remoteLeaf.DeleteMarkerAsync(".uploading");
            }

            await localLeaf.WriteMarkerAsync(".synched");
        }
        else
        {
            // Node folder
            var localEntries = Directory.GetFileSystemEntries(localPath)
                .Where(e => !VersionedStorageEngine.IgnoredSystemFiles.Any(r => r.IsMatch(Path.GetFileName(e))))
                .ToList();

            var remoteChildren = await fileSystem.ListChildrenAsync(remoteFolderId, parameters);

            foreach (var entry in localEntries.Where(e => Directory.Exists(e)))
            {
                if (File.Exists(Path.Combine(entry, ".synched"))) continue;

                var name = Path.GetFileName(entry);
                var remoteMatch = remoteChildren.FirstOrDefault(c => c.Name == name && c.IsFolder);
                string folderId;
                if (remoteMatch != null)
                {
                    folderId = remoteMatch.Id;
                }
                else
                {
                    var newFolder = await fileSystem.CreateFolderAsync(remoteFolderId, name, parameters);
                    if (newFolder == null) continue;
                    folderId = newFolder.Id;
                }
                await SyncUpAsync(entry, folderId, parameters, fileSystem);
            }
        }
    }
}
