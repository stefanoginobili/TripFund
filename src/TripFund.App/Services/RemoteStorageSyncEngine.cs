using System.Net.Http.Headers;
using System.IO.Compression;
using TripFund.App.Models;
using TripFund.App.Constants;

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

            var settings = await _localStorage.GetAppSettingsAsync();
            var deviceId = settings?.DeviceId ?? "unknown-device";

            logger.LogInfo("Authenticating with remote storage provider...");
            await fileSystem.EnsureAuthenticatedAsync(entry.RemoteStorage.Parameters);
            await _localStorage.SaveTripRegistryAsync(registry);

            // 1. Evaluation Phase
            logger.LogInfo("Phase 1: EVALUATION");
            var devicesFolder = await GetOrCreateFolderAsync(folderId, "devices", entry.RemoteStorage.Parameters, fileSystem);
            var packagesFolder = await GetOrCreateFolderAsync(folderId, "packages", entry.RemoteStorage.Parameters, fileSystem);

            if (devicesFolder == null || packagesFolder == null)
            {
                throw new Exception("Failed to initialize remote folder structure.");
            }

            var deviceRoot = await GetOrCreateFolderAsync(devicesFolder.Id, deviceId, entry.RemoteStorage.Parameters, fileSystem);
            if (deviceRoot == null)
            {
                throw new Exception($"Failed to create device folder for {deviceId}");
            }

            var canWrite = await CheckWritePermissionAsync(deviceRoot.Id, entry.RemoteStorage.Parameters, fileSystem);
            if (entry.RemoteStorage.Readonly != !canWrite)
            {
                entry.RemoteStorage.Readonly = !canWrite;
                await _localStorage.SaveTripRegistryAsync(registry);
            }
            logger.LogInfo($"Remote storage is {(entry.RemoteStorage.Readonly ? "READ-ONLY" : "READ-WRITE")}");

            var syncState = await _localStorage.GetSyncStateAsync(tripSlug);
            var localTripPath = Path.Combine(_localStorage.TripsPath, tripSlug);

            // 2. Download Phase
            logger.LogInfo("Phase 2: DOWNLOAD");
            var remotePackages = await fileSystem.ListChildrenAsync(packagesFolder.Id, entry.RemoteStorage.Parameters);
            
            // Filter and sort:
            // - Starts with pack_
            // - Ends with .zip
            // - Does NOT contain local DeviceId
            // - Not already in appliedPackages
            var toDownload = remotePackages
                .Where(p => p.Name.StartsWith("pack_", StringComparison.OrdinalIgnoreCase))
                .Where(p => p.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .Where(p => !p.Name.Contains($"_{deviceId}.zip", StringComparison.OrdinalIgnoreCase))
                .Where(p => !syncState.Sync.Remote.AppliedPackages.Contains(p.Name))
                .OrderBy(p => p.Name)
                .ToList();

            if (toDownload.Count > 0)
            {
                logger.LogInfo($"Found {toDownload.Count} new packages to apply.");
                var tempPath = Path.Combine(localTripPath, "temp", "packages");
                var downloadPath = Path.Combine(tempPath, "downloaded");
                var expandedPath = Path.Combine(tempPath, "expanded");
                
                if (Directory.Exists(tempPath)) Directory.Delete(tempPath, true);
                Directory.CreateDirectory(downloadPath);
                Directory.CreateDirectory(expandedPath);

                foreach (var package in toDownload)
                {
                    logger.LogInfo($"Downloading package {package.Name}...");
                    var zipBytes = await fileSystem.DownloadFileContentAsync(package.Id, entry.RemoteStorage.Parameters);
                    if (zipBytes == null) throw new Exception($"Failed to download package {package.Name}");

                    var zipFile = Path.Combine(downloadPath, package.Name);
                    await File.WriteAllBytesAsync(zipFile, zipBytes);

                    // Extract into expanded folder
                    try
                    {
                        using (var archive = ZipFile.OpenRead(zipFile))
                        {
                            foreach (var zipEntry in archive.Entries)
                            {
                                if (string.IsNullOrEmpty(zipEntry.Name)) continue; // Directory entry

                                var entryPath = zipEntry.FullName.Replace('\\', '/');
                                var targetFile = Path.Combine(expandedPath, entryPath);
                                var targetDir = Path.GetDirectoryName(targetFile);
                                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                                zipEntry.ExtractToFile(targetFile, true);
                            }
                        }
                        // Package is kept in 'downloaded' until the end of the phase for clarity, 
                        // or deleted here if memory/storage is an issue. Keeping it simple as per request.
                    }
                    catch (Exception ex)
                    {
                        logger.LogError($"Failed to extract package {package.Name}", ex);
                        throw;
                    }
                }

                // Move expanded folders to final destination
                logger.LogInfo("Applying extracted updates to local storage...");
                await MoveExpandedFoldersAsync(expandedPath, localTripPath);

                // Update sync state
                foreach (var package in toDownload)
                {
                    syncState.Sync.Remote.AppliedPackages.Add(package.Name);
                }
                await _localStorage.SaveSyncStateAsync(tripSlug, syncState);
                
                // Cleanup expanded path
                Directory.Delete(tempPath, true);
            }
            else
            {
                logger.LogInfo("No new remote packages found.");
            }

            // 3. Conflict Check (Blocks Upload)
            logger.LogInfo("Checking for local conflicts...");
            var conflicts = await GetLocalConflictsAsync(localTripPath);
            if (conflicts.Count > 0)
            {
                logger.LogInfo($"Conflict detected ({conflicts.Count}). Sync upload aborted. Resolution required.");
                throw new SyncConflictException(conflicts);
            }

            // 4. Upload Phase
            if (!entry.RemoteStorage.Readonly)
            {
                if (syncState.Sync.Local.Pending.Count > 0)
                {
                    logger.LogInfo($"Phase 3: UPLOAD ({syncState.Sync.Local.Pending.Count} pending folders)");
                    
                    var rawTimestamp = syncState.Sync.Local.Pending.Min(u => u.CreatedAt);
                    var timestamp = rawTimestamp;
                    if (DateTime.TryParse(rawTimestamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    {
                        timestamp = dt.ToString("yyyyMMddTHHmmssfffZ", System.Globalization.CultureInfo.InvariantCulture);
                    }
                    var packageName = $"pack_{timestamp}_{deviceId}.zip";
                    var tempZipPath = Path.Combine(localTripPath, "temp", $"{packageName}.tmp");
                    var tempDir = Path.GetDirectoryName(tempZipPath);
                    if (!string.IsNullOrEmpty(tempDir) && !Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                    if (File.Exists(tempZipPath)) File.Delete(tempZipPath);

                    var uploadedPaths = new List<string>();

                    using (var archive = ZipFile.Open(tempZipPath, ZipArchiveMode.Create))
                    {
                        foreach (var pending in syncState.Sync.Local.Pending)
                        {
                            var fullPath = Path.Combine(localTripPath, pending.Path);
                            var normalizedPendingPath = pending.Path.Replace('\\', '/');
                            if (!Directory.Exists(fullPath))
                            {
                                logger.LogWarning($"Pending folder {pending.Path} not found locally. Skipping.");
                                uploadedPaths.Add(pending.Path);
                                continue;
                            }

                            // Add metadata (.tripfund)
                            var metaPath = Path.Combine(fullPath, AppConstants.Files.TripFundFile);
                            if (File.Exists(metaPath)) archive.CreateEntryFromFile(metaPath, $"{normalizedPendingPath}/{AppConstants.Files.TripFundFile}");

                            // Add content (.content)
                            var dataDir = Path.Combine(fullPath, AppConstants.Files.ContentFolder);
                            if (Directory.Exists(dataDir))
                            {
                                foreach (var file in Directory.GetFiles(dataDir))
                                {
                                    archive.CreateEntryFromFile(file, $"{normalizedPendingPath}/{AppConstants.Files.ContentFolder}/{Path.GetFileName(file)}");
                                }
                            }
                            
                            uploadedPaths.Add(pending.Path);
                        }
                    }

                    if (uploadedPaths.Count > 0)
                    {
                        var zipContent = await File.ReadAllBytesAsync(tempZipPath);
                        
                        var uploaded = await fileSystem.UploadFileAsync(packagesFolder.Id, packageName, zipContent, entry.RemoteStorage.Parameters);
                        if (uploaded == null) throw new Exception("Failed to upload ZIP package.");

                        // Success: cleanup local state
                        syncState.Sync.Local.Pending.RemoveAll(u => uploadedPaths.Contains(u.Path));
                        await _localStorage.SaveSyncStateAsync(tripSlug, syncState);
                        File.Delete(tempZipPath);
                        logger.LogInfo($"Uploaded {uploadedPaths.Count} folders in package {packageName}");
                    }
                }
                else
                {
                    logger.LogInfo("Skipping UPLOAD phase (no local changes).");
                }
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
            await SaveSyncLogLocallyAsync(tripSlug, fileSystem);
            await _localStorage.CleanupTempFoldersAsync();
        }
    }

    private async Task MoveExpandedFoldersAsync(string sourceRoot, string targetRoot)
    {
        // Recursively find leaf folders in expanded/ and move them to final destination.
        // Leaf folders are identified by containing a .tripfund file (as per ARCHITECTURE.md).
        var directories = Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories);
        foreach (var dir in directories)
        {
            if (File.Exists(Path.Combine(dir, AppConstants.Files.TripFundFile)))
            {
                var relativePath = Path.GetRelativePath(sourceRoot, dir);
                var finalPath = Path.Combine(targetRoot, relativePath);
                var finalDir = Path.GetDirectoryName(finalPath);
                if (!string.IsNullOrEmpty(finalDir) && !Directory.Exists(finalDir)) Directory.CreateDirectory(finalDir);

                if (Directory.Exists(finalPath)) Directory.Delete(finalPath, true);
                Directory.Move(dir, finalPath);
            }
        }
        await Task.CompletedTask;
    }

    private bool HasPendingUploads(string tripSlug)
    {
        var task = _localStorage.GetSyncStateAsync(tripSlug);
        task.Wait();
        return task.Result.Sync.Local.Pending.Count > 0;
    }

    private async Task SaveSyncLogLocallyAsync(string tripSlug, IRemoteFileSystem fileSystem)
    {
        if (fileSystem.Logger == null) return;

        var logContent = fileSystem.Logger.GetLogContent();

        // The requirement is trips/[TripSlug]/sync/logs/{yyyyMMddTHHmmssZ}.log
        var logsDir = Path.Combine(_localStorage.TripsPath, tripSlug, "sync", "logs");
        if (!Directory.Exists(logsDir))
        {
            Directory.CreateDirectory(logsDir);
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var logFile = Path.Combine(logsDir, $"{timestamp}.log");
        await File.WriteAllTextAsync(logFile, logContent);

        // Rotate logs: keep only the last 20
        var logFiles = Directory.GetFiles(logsDir, "*.log")
            .OrderByDescending(f => f)
            .ToList();

        if (logFiles.Count > 20)
        {
            foreach (var file in logFiles.Skip(20))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Ignore deletion errors for logs
                }
            }
        }
    }

    private async Task<RemoteItem?> GetOrCreateFolderAsync(string parentId, string name, Dictionary<string, string> parameters, IRemoteFileSystem fileSystem)
    {
        var children = await fileSystem.ListChildrenAsync(parentId, parameters);
        var existing = children.FirstOrDefault(c => c.Name == name && c.IsFolder);
        if (existing != null) return existing;

        return await fileSystem.CreateFolderAsync(parentId, name, parameters);
    }

    private async Task<bool> CheckWritePermissionAsync(string folderId, Dictionary<string, string> parameters, IRemoteFileSystem fileSystem)
    {
        try
        {
            var fileName = ".last-seen";
            var content = System.Text.Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
            var uploaded = await fileSystem.UploadFileAsync(folderId, fileName, content, parameters);
            return uploaded != null;
        }
        catch
        {
            return false;
        }
    }

    private (string Timestamp, string DeviceId) ParsePackageName(string name)
    {
        // pack_20260413T143255890Z_{DeviceId1}.zip
        var parts = name.Replace(".zip", "").Split('_');
        if (parts.Length < 3) return ("unknown", "unknown");
        return (parts[1], parts[2]);
    }

    private async Task<List<VersionedFolderConflictException>> GetLocalConflictsAsync(string localTripPath)
    {
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
        return conflicts;
    }
}
