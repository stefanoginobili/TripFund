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
        onSyncStateChanged?.Invoke(tripSlug, true);
        try
        {
            var registry = await _localStorage.GetTripRegistryAsync();
            if (!registry.Trips.TryGetValue(tripSlug, out var entry) || entry.RemoteStorage == null) return;

            if (!entry.RemoteStorage.Parameters.TryGetValue("folderId", out var folderId)) return;

            await fileSystem.EnsureAuthenticatedAsync(entry.RemoteStorage.Parameters);
            // After authentication, check if registry needs to be saved (e.g., updated refresh token)
            await _localStorage.SaveTripRegistryAsync(registry);

            // Check if root is readonly
            var canWrite = await CheckFolderWritePermissionAsync(folderId, entry.RemoteStorage.Parameters, fileSystem);
            if (entry.RemoteStorage.Readonly != !canWrite)
            {
                entry.RemoteStorage.Readonly = !canWrite;
                await _localStorage.SaveTripRegistryAsync(registry);
            }

            // 1. Download Phase
            var localTripPath = Path.Combine(_localStorage.TripsPath, tripSlug);
            await SyncDownAsync(folderId, localTripPath, entry.RemoteStorage.Parameters, fileSystem);

            // 2. Integrity & Conflict Check
            bool hasConflict = false;
            if (Directory.Exists(Path.Combine(localTripPath, "metadata")))
            {
                if (_engine.IsInConflict(Path.Combine(localTripPath, "metadata"))) hasConflict = true;
            }

            var transDir = Path.Combine(localTripPath, "transactions");
            if (Directory.Exists(transDir))
            {
                foreach (var t in Directory.GetDirectories(transDir))
                {
                    if (_engine.IsInConflict(t))
                    {
                        hasConflict = true;
                        break;
                    }
                }
            }

            entry.RemoteStorage.HasConflicts = hasConflict;
            await _localStorage.SaveTripRegistryAsync(registry);

            if (hasConflict) return;

            // 3. Upload Phase
            if (!entry.RemoteStorage.Readonly)
            {
                await SyncUpAsync(localTripPath, folderId, entry.RemoteStorage.Parameters, fileSystem);
            }
        }
        finally
        {
            onSyncStateChanged?.Invoke(tripSlug, false);
        }
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

        var children = await fileSystem.ListChildrenAsync(remoteFolderId, parameters);
        if (children.Count == 0) return;

        bool hasFolders = children.Any(c => c.IsFolder);
        bool hasFiles = children.Any(c => !c.IsFolder && c.Name != ".synching");

        if (hasFolders && hasFiles)
        {
            throw new InvalidOperationException("Architecture constraint violation: Folder contains both files and subfolders.");
        }

        if (hasFolders)
        {
            foreach (var child in children.Where(c => c.IsFolder))
            {
                var localChildPath = Path.Combine(localPath, child.Name);
                if (File.Exists(Path.Combine(localChildPath, ".synched"))) continue;

                if (!Directory.Exists(localChildPath)) Directory.CreateDirectory(localChildPath);
                await SyncDownAsync(child.Id, localChildPath, parameters, fileSystem);
            }
        }
        else if (hasFiles)
        {
            bool isFullyCopiedLocally = false;
            if (Directory.Exists(localPath))
            {
                var localEntries = Directory.GetFileSystemEntries(localPath)
                    .Where(e => !e.EndsWith(".remote-etag") && Path.GetFileName(e) != ".synching" && Path.GetFileName(e) != ".synched")
                    .ToList();
                bool hasSynching = File.Exists(Path.Combine(localPath, ".synching"));
                if (localEntries.Count > 0 && !hasSynching)
                {
                    isFullyCopiedLocally = true;
                }
            }

            if (!isFullyCopiedLocally)
            {
                if (Directory.Exists(localPath))
                {
                    foreach (var file in Directory.GetFiles(localPath)) File.Delete(file);
                }
                else
                {
                    Directory.CreateDirectory(localPath);
                }

                var synchingFile = Path.Combine(localPath, ".synching");
                await File.WriteAllTextAsync(synchingFile, "");

                foreach (var child in children.Where(c => !c.IsFolder && c.Name != ".synching"))
                {
                    var localChildFile = Path.Combine(localPath, child.Name);
                    var content = await fileSystem.DownloadFileContentAsync(child.Id, parameters);
                    if (content != null)
                    {
                        await File.WriteAllBytesAsync(localChildFile, content);
                        await File.WriteAllTextAsync(localChildFile + ".remote-etag", child.ETag);
                    }
                }

                if (File.Exists(synchingFile)) File.Delete(synchingFile);
            }
            else
            {
                foreach (var child in children.Where(c => !c.IsFolder && c.Name != ".synching"))
                {
                    var localChildFile = Path.Combine(localPath, child.Name);
                    var metadataFile = localChildFile + ".remote-etag";
                    var remoteEtag = child.ETag;
                    var localEtag = File.Exists(metadataFile) ? await File.ReadAllTextAsync(metadataFile) : null;

                    if (remoteEtag != localEtag)
                    {
                        var content = await fileSystem.DownloadFileContentAsync(child.Id, parameters);
                        if (content != null)
                        {
                            await File.WriteAllBytesAsync(localChildFile, content);
                            await File.WriteAllTextAsync(metadataFile, remoteEtag);
                        }
                    }
                }
            }

            await File.WriteAllTextAsync(Path.Combine(localPath, ".synched"), "");
        }
    }

    private async Task SyncUpAsync(string localPath, string remoteFolderId, Dictionary<string, string> parameters, IRemoteFileSystem fileSystem)
    {
        if (File.Exists(Path.Combine(localPath, ".synched"))) return;

        var localEntries = Directory.GetFileSystemEntries(localPath)
            .Where(e => !e.EndsWith(".remote-etag") && Path.GetFileName(e) != ".synching" && Path.GetFileName(e) != ".synched")
            .ToList();

        // OPTIMIZATION: Filter out items that are already synched. 
        // Files won't be filtered as they don't contain a ".synched" file.
        localEntries = localEntries.Where(e => !File.Exists(Path.Combine(e, ".synched"))).ToList();
        
        if (localEntries.Count == 0) return;

        bool hasFolders = localEntries.Any(e => Directory.Exists(e));
        bool hasFiles = localEntries.Any(e => File.Exists(e));

        if (hasFolders && hasFiles)
        {
            throw new InvalidOperationException("Architecture constraint violation: Folder contains both files and subfolders.");
        }

        var remoteChildren = await fileSystem.ListChildrenAsync(remoteFolderId, parameters);

        if (hasFolders)
        {
            foreach (var entry in localEntries.Where(e => Directory.Exists(e)))
            {
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
        else if (hasFiles)
        {
            bool isFullyCopiedRemotely = false;
            bool hasSynching = remoteChildren.Any(c => c.Name == ".synching");
            bool isEmpty = !remoteChildren.Any(c => c.Name != ".synching");

            if (!isEmpty && !hasSynching)
            {
                isFullyCopiedRemotely = true;
            }

            if (!isFullyCopiedRemotely)
            {
                foreach (var child in remoteChildren)
                {
                    await fileSystem.DeleteFileAsync(child.Id, parameters);
                }

                await fileSystem.UploadFileAsync(remoteFolderId, ".synching", new byte[] { 0x01 }, parameters);

                foreach (var entry in localEntries.Where(e => File.Exists(e)))
                {
                    var name = Path.GetFileName(entry);
                    var content = await File.ReadAllBytesAsync(entry);
                    var uploaded = await fileSystem.UploadFileAsync(remoteFolderId, name, content, parameters);
                    if (uploaded != null)
                    {
                        await File.WriteAllTextAsync(entry + ".remote-etag", uploaded.ETag);
                    }
                }

                var finalChildren = await fileSystem.ListChildrenAsync(remoteFolderId, parameters);
                var sFile = finalChildren.FirstOrDefault(c => c.Name == ".synching");
                if (sFile != null) await fileSystem.DeleteFileAsync(sFile.Id, parameters);
            }
            else
            {
                foreach (var entry in localEntries.Where(e => File.Exists(e)))
                {
                    var name = Path.GetFileName(entry);
                    var remoteMatch = remoteChildren.FirstOrDefault(c => c.Name == name);

                    var content = await File.ReadAllBytesAsync(entry);
                    var etagFile = entry + ".remote-etag";
                    var localEtag = File.Exists(etagFile) ? await File.ReadAllTextAsync(etagFile) : null;

                    if (remoteMatch == null || localEtag == null)
                    {
                        var uploaded = await fileSystem.UploadFileAsync(remoteFolderId, name, content, parameters);
                        if (uploaded != null) await File.WriteAllTextAsync(etagFile, uploaded.ETag);
                    }
                }
            }

            await File.WriteAllTextAsync(Path.Combine(localPath, ".synched"), "");
        }
    }
}
