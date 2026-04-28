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
            
            RemoteItem? devicesFolder = null;
            RemoteItem? packagesFolder = null;

            // Try to ensure remote structure exists.
            try
            {
                devicesFolder = await GetOrCreateFolderAsync(folderId, AppConstants.Folders.Devices, entry.RemoteStorage.Parameters, fileSystem);
                packagesFolder = await GetOrCreateFolderAsync(folderId, AppConstants.Folders.Packages, entry.RemoteStorage.Parameters, fileSystem);
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Could not ensure remote structure (might be READ-ONLY): {ex.Message}");
                // Fallback: peek if they already exist
                var rootChildren = await fileSystem.ListChildrenAsync(folderId, entry.RemoteStorage.Parameters);
                devicesFolder = rootChildren.FirstOrDefault(c => c.Name == AppConstants.Folders.Devices && c.IsFolder);
                packagesFolder = rootChildren.FirstOrDefault(c => c.Name == AppConstants.Folders.Packages && c.IsFolder);
            }

            if (packagesFolder == null)
            {
                if (entry.RemoteStorage.Readonly)
                {
                    logger.LogInfo($"Remote '{AppConstants.Folders.Packages}' folder not found. Nothing to download yet.");
                }
                else
                {
                    throw new Exception($"Remote '{AppConstants.Folders.Packages}' folder not found. Has the trip been initialized by the owner?");
                }
            }

            // Attempt to register this device and verify permissions
            if (devicesFolder != null)
            {
                try
                {
                    var deviceRoot = await GetOrCreateFolderAsync(devicesFolder.Id, deviceId, entry.RemoteStorage.Parameters, fileSystem);
                    if (deviceRoot != null)
                    {
                        var canWrite = await CheckWritePermissionAsync(deviceRoot.Id, entry.RemoteStorage.Parameters, fileSystem);
                        
                        // Sync the local status with the detected status
                        if (entry.RemoteStorage.Readonly == canWrite) 
                        {
                            entry.RemoteStorage.Readonly = !canWrite;
                            await _localStorage.SaveTripRegistryAsync(registry);
                        }
                    }
                    else if (!entry.RemoteStorage.Readonly)
                    {
                        logger.LogInfo("Could not create device folder. Setting to READ-ONLY.");
                        entry.RemoteStorage.Readonly = true;
                        await _localStorage.SaveTripRegistryAsync(registry);
                    }
                }
                catch (Exception ex)
                {
                    if (!entry.RemoteStorage.Readonly)
                    {
                        logger.LogWarning($"Error during device initialization: {ex.Message}. Setting to READ-ONLY.");
                        entry.RemoteStorage.Readonly = true;
                        await _localStorage.SaveTripRegistryAsync(registry);
                    }
                }
            }
            else if (!entry.RemoteStorage.Readonly)
            {
                logger.LogInfo("Remote 'devices' folder missing and could not be created. Setting to READ-ONLY.");
                entry.RemoteStorage.Readonly = true;
                await _localStorage.SaveTripRegistryAsync(registry);
            }
            
            logger.LogInfo($"Remote storage is {(entry.RemoteStorage.Readonly ? "READ-ONLY" : "READ-WRITE")}");

            var syncState = await _localStorage.GetSyncStateAsync(tripSlug);
            var localTripPath = Path.Combine(_localStorage.TripsPath, tripSlug);

            // 2. Download Phase
            logger.LogInfo("Phase 2: DOWNLOAD");
            
            if (packagesFolder != null)
            {
                var remotePackages = await fileSystem.ListChildrenAsync(packagesFolder.Id, entry.RemoteStorage.Parameters);
                
                var isInitialImport = File.Exists(Path.Combine(localTripPath, AppConstants.Files.InitialImportMarker));

                var toDownload = remotePackages
                    .Where(p => p.Name.StartsWith("pack_", StringComparison.OrdinalIgnoreCase))
                    .Where(p => p.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    .Where(p => isInitialImport || !p.Name.Contains($"_{deviceId}.zip", StringComparison.OrdinalIgnoreCase))
                    .Where(p => !syncState.Sync.Remote.AppliedPackages.Contains(p.Name))
                    .OrderBy(p => p.Name)
                    .ToList();

                if (toDownload.Count > 0)
                {
                    logger.LogInfo($"Found {toDownload.Count} new packages to apply.");
                    var tempPath = Path.Combine(localTripPath, AppConstants.Folders.Temp, AppConstants.Folders.Packages);
                    var downloadPath = Path.Combine(tempPath, AppConstants.Folders.Inbox);
                    var expandedPath = Path.Combine(tempPath, AppConstants.Folders.Expanded);
                    
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

                        try
                        {
                            using (var archive = ZipFile.OpenRead(zipFile))
                            {
                                foreach (var zipEntry in archive.Entries)
                                {
                                    if (string.IsNullOrEmpty(zipEntry.Name)) continue;

                                    var entryPath = zipEntry.FullName.Replace('\\', '/');
                                    var targetFile = Path.Combine(expandedPath, entryPath);
                                    var targetDir = Path.GetDirectoryName(targetFile);
                                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                                    zipEntry.ExtractToFile(targetFile, true);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError($"Failed to extract package {package.Name}", ex);
                            throw;
                        }
                    }

                    logger.LogInfo("Applying extracted updates to local storage...");
                    var impactedRoots = new HashSet<string>();
                    await MoveExpandedFoldersAsync(expandedPath, localTripPath, impactedRoots);

                    if (impactedRoots.Count > 0)
                    {
                        logger.LogInfo($"Recalculating heads for {impactedRoots.Count} impacted versioned folders...");
                        await _localStorage.UpdateVersionHeadsAfterSyncAsync(tripSlug, impactedRoots);
                    }

                    foreach (var package in toDownload)
                    {
                        syncState.Sync.Remote.AppliedPackages.Add(package.Name);
                    }
                    await _localStorage.SaveSyncStateAsync(tripSlug, syncState);
                    Directory.Delete(tempPath, true);
                }
                else
                {
                    logger.LogInfo("No new remote packages found.");
                }
            }
            else
            {
                logger.LogInfo("Skipping download phase: packages folder is not available.");
            }

            // 3. Conflict Check & Upload Phase
            if (!entry.RemoteStorage.Readonly)
            {
                logger.LogInfo("Checking for local conflicts...");
                var conflicts = await GetLocalConflictsAsync(localTripPath);
                if (conflicts.Count > 0)
                {
                    logger.LogInfo($"Conflict detected ({conflicts.Count}). Sync upload aborted. Resolution required.");
                    throw new SyncConflictException(conflicts);
                }

                if (syncState.Sync.Local.Pending.Count > 0)
                {
                    logger.LogInfo($"Phase 3: UPLOAD ({syncState.Sync.Local.Pending.Count} pending folders)");
                    
                    var rawTimestamp = syncState.Sync.Local.Pending.Min(u => u.CreatedAt);
                    DateTime timestampDt = DateTime.UtcNow;
                    if (DateTime.TryParse(rawTimestamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    {
                        timestampDt = dt;
                    }
                    
                    var packageName = string.Format(AppConstants.Files.RemotePackageTemplate, timestampDt, deviceId);
                    var tempZipPath = Path.Combine(localTripPath, AppConstants.Folders.Temp, AppConstants.Folders.Packages, AppConstants.Folders.Outbox, packageName);
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

                            var metaPath = Path.Combine(fullPath, AppConstants.Files.TripFundFile);
                            if (File.Exists(metaPath)) archive.CreateEntryFromFile(metaPath, $"{normalizedPendingPath}/{AppConstants.Files.TripFundFile}");

                            var dataDir = Path.Combine(fullPath, AppConstants.Folders.Content);
                            if (Directory.Exists(dataDir))
                            {
                                foreach (var file in Directory.GetFiles(dataDir))
                                {
                                    archive.CreateEntryFromFile(file, $"{normalizedPendingPath}/{AppConstants.Folders.Content}/{Path.GetFileName(file)}");
                                }
                            }
                            
                            uploadedPaths.Add(pending.Path);
                        }
                    }

                    if (uploadedPaths.Count > 0)
                    {
                        var zipContent = await File.ReadAllBytesAsync(tempZipPath);
                        
                        if (packagesFolder == null) throw new Exception("Packages folder missing during upload.");

                        var uploaded = await fileSystem.UploadFileAsync(packagesFolder.Id, packageName, zipContent, entry.RemoteStorage.Parameters);
                        if (uploaded == null) throw new Exception("Failed to upload ZIP package.");

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

    private async Task MoveExpandedFoldersAsync(string sourceRoot, string targetRoot, HashSet<string>? impactedRoots = null)
    {
        var directories = Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories);
        foreach (var dir in directories)
        {
            if (File.Exists(Path.Combine(dir, AppConstants.Files.TripFundFile)))
            {
                var relativePath = Path.GetRelativePath(sourceRoot, dir);
                var finalPath = Path.Combine(targetRoot, relativePath);
                
                if (impactedRoots != null)
                {
                    // If the path contains ".versions", the versioned storage root is its parent
                    var normalizedPath = relativePath.Replace('\\', '/');
                    var parts = normalizedPath.Split('/');
                    int versionsIdx = Array.IndexOf(parts, AppConstants.Folders.Versions);
                    if (versionsIdx > 0)
                    {
                        var rootRelative = string.Join(Path.DirectorySeparatorChar, parts.Take(versionsIdx));
                        impactedRoots.Add(Path.Combine(targetRoot, rootRelative));
                    }
                }

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
        var logsDir = Path.Combine(_localStorage.TripsPath, tripSlug, AppConstants.Folders.Sync, AppConstants.Folders.Logs);
        if (!Directory.Exists(logsDir))
        {
            Directory.CreateDirectory(logsDir);
        }

        var timestampDt = DateTime.UtcNow;
        var logFile = Path.Combine(logsDir, string.Format(AppConstants.Files.SyncLogTemplate, timestampDt));
        await File.WriteAllTextAsync(logFile, logContent);

        var logFiles = Directory.GetFiles(logsDir, AppConstants.Files.SyncLogRotationFilePattern)
            .OrderByDescending(f => f)
            .ToList();

        if (logFiles.Count > 20)
        {
            foreach (var file in logFiles.Skip(20))
            {
                try { File.Delete(file); } catch { }
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
            var fileName = AppConstants.Files.LastSeen;
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
        var parts = name.Replace(".zip", "").Split('_');
        if (parts.Length < 3) return ("unknown", "unknown");
        return (parts[1], parts[2]);
    }

    private async Task<List<VersionedFolderConflictException>> GetLocalConflictsAsync(string localTripPath)
    {
        var conflicts = new List<VersionedFolderConflictException>();
        var configPath = Path.Combine(localTripPath, AppConstants.Folders.Config);
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
        var transDir = Path.Combine(localTripPath, AppConstants.Folders.Transactions);
        if (Directory.Exists(transDir))
        {
            foreach (var t in Directory.GetDirectories(transDir))
            {
                var detailsPath = Path.Combine(t, AppConstants.Folders.Details);
                if (Directory.Exists(detailsPath))
                {
                    var latest = _engine.GetLatestVersionFolders(detailsPath);
                    if (latest.Count > 1)
                    {
                        var diverging = latest.Select(v => v.FolderName).ToList();
                        var baseVer = _engine.GetBaseVersionFolder(detailsPath, latest[0].Sequence);
                        conflicts.Add(new TransactionConflictException(Path.GetFileName(t), diverging, baseVer));
                    }
                }
            }
        }
        return conflicts;
    }
}
