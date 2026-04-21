using System.Net.Http.Headers;
using System.IO.Compression;
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
                if (!Directory.Exists(tempPath)) Directory.CreateDirectory(tempPath);

                foreach (var package in toDownload)
                {
                    logger.LogInfo($"Applying package {package.Name}...");
                    var zipBytes = await fileSystem.DownloadFileContentAsync(package.Id, entry.RemoteStorage.Parameters);
                    if (zipBytes == null) throw new Exception($"Failed to download package {package.Name}");

                    var zipFile = Path.Combine(tempPath, package.Name);
                    await File.WriteAllBytesAsync(zipFile, zipBytes);

                    // Extract and Merge
                    try
                    {
                        using (var archive = ZipFile.OpenRead(zipFile))
                        {
                            foreach (var zipEntry in archive.Entries)
                            {
                                if (string.IsNullOrEmpty(zipEntry.Name)) continue; // Directory entry

                                var entryPath = zipEntry.FullName.Replace('\\', '/');
                                var targetFile = Path.Combine(localTripPath, entryPath);
                                var targetDir = Path.GetDirectoryName(targetFile);
                                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                                zipEntry.ExtractToFile(targetFile, true);
                            }
                        }

                        // Mark extracted leaf folders as .active
                        await MarkExtractedLeavesAsActiveAsync(zipFile);

                        // Update sync state
                        syncState.Sync.Remote.AppliedPackages.Add(package.Name);

                        await _localStorage.SaveSyncStateAsync(tripSlug, syncState);
                        File.Delete(zipFile);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError($"Failed to apply package {package.Name}", ex);
                        throw;
                    }
                }
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

                            // Only pack if .active is present
                            if (!File.Exists(Path.Combine(fullPath, ".active")))
                            {
                                logger.LogWarning($"Pending folder {pending.Path} is not marked as .active. Skipping.");
                                continue;
                            }

                            // Add .metadata
                            var metaPath = Path.Combine(fullPath, ".metadata");
                            if (File.Exists(metaPath)) archive.CreateEntryFromFile(metaPath, $"{normalizedPendingPath}/.metadata");

                            // Add .data content
                            var dataDir = Path.Combine(fullPath, ".data");
                            if (Directory.Exists(dataDir))
                            {
                                foreach (var file in Directory.GetFiles(dataDir))
                                {
                                    archive.CreateEntryFromFile(file, $"{normalizedPendingPath}/.data/{Path.GetFileName(file)}");
                                }
                            }
                            
                            uploadedPaths.Add(pending.Path);
                        }
                    }

                    if (uploadedPaths.Count > 0)
                    {
                        var zipContent = await File.ReadAllBytesAsync(tempZipPath);
                        var partName = packageName + ".part";
                        
                        // Clean up previous partial uploads
                        var existingPart = (await fileSystem.ListChildrenAsync(packagesFolder.Id, entry.RemoteStorage.Parameters))
                            .FirstOrDefault(p => p.Name == partName);
                        if (existingPart != null) await fileSystem.DeleteFileAsync(existingPart.Id, entry.RemoteStorage.Parameters);

                        var uploaded = await fileSystem.UploadFileAsync(packagesFolder.Id, partName, zipContent, entry.RemoteStorage.Parameters);
                        if (uploaded == null) throw new Exception("Failed to upload ZIP package.");

                        await fileSystem.RenameAsync(uploaded.Id, packageName, entry.RemoteStorage.Parameters);
                        
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
        }
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

    private async Task MarkExtractedLeavesAsActiveAsync(string zipFilePath)
    {
        var rootPath = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(zipFilePath))); // Up from temp/packages/
        if (rootPath == null) return;

        using var archive = ZipFile.OpenRead(zipFilePath);
        var leafFolders = archive.Entries
            .Select(e => e.FullName.Replace('\\', '/'))
            .Select(f => Path.GetDirectoryName(f))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct();

        foreach (var folder in leafFolders)
        {
            var activeMarker = Path.Combine(rootPath, folder!, ".active");
            if (!File.Exists(activeMarker))
            {
                await File.WriteAllTextAsync(activeMarker, "");
            }
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
