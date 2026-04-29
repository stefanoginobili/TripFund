using System.Text.RegularExpressions;
using TripFund.App.Models;
using TripFund.App.Constants;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TripFund.Tests")]

namespace TripFund.App.Services;

public enum CommitKind
{
    New,
    Upd,
    Res,
    Del
}

public class VersionFolderInfo
{
    public string FolderName { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public CommitKind Kind { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public IReadOnlyList<string> ParentVersions { get; set; } = new List<string>();
}

public class VersionedFolderConflictException : Exception
{
    public IReadOnlyList<string> DivergingVersions { get; }
    public string? BaseVersion { get; }

    public VersionedFolderConflictException(string message, IReadOnlyList<string> divergingVersions, string? baseVersion)
        : base(message)
    {
        DivergingVersions = divergingVersions;
        BaseVersion = baseVersion;
    }
}

public class TripConfigConflictException : VersionedFolderConflictException
{
    public TripConfigConflictException(IReadOnlyList<string> divergingVersions, string? baseVersion)
        : base("Trip config conflict detected.", divergingVersions, baseVersion)
    {
    }
}

public class TransactionConflictException : VersionedFolderConflictException
{
    public string TransactionId { get; }

    public TransactionConflictException(string transactionId, IReadOnlyList<string> divergingVersions, string? baseVersion)
        : base($"Conflict detected in transaction {transactionId}.", divergingVersions, baseVersion)
    {
        TransactionId = transactionId;
    }
}

public class SyncConflictException : Exception
{
    public IReadOnlyList<VersionedFolderConflictException> Conflicts { get; }

    public SyncConflictException(IReadOnlyList<VersionedFolderConflictException> conflicts)
        : base($"Synchronization aborted due to {conflicts.Count} conflicts.")
    {
        Conflicts = conflicts;
    }
}

public class VersionedStorageEngine
{
    private static readonly Regex VersionRegex = new(@"^(?<nnn>\d{3})_(?<kind>NEW|UPD|RES|DEL|new|upd|res|del)_(?<deviceId>[a-z0-9\-]+)$", RegexOptions.Compiled);

    private readonly string _rootPath;
    private readonly string _deviceId;
    private readonly string _author;

    public VersionedStorageEngine(string rootPath, string deviceId, string author)
    {
        _rootPath = rootPath;
        _deviceId = deviceId;
        _author = author;
    }

    public List<VersionFolderInfo> GetVersionFolders()
    {
        string versionsPath = Path.Combine(_rootPath, AppConstants.Folders.Versions);
        if (!Directory.Exists(versionsPath)) return new List<VersionFolderInfo>();

        var folderNames = Directory.GetDirectories(versionsPath).Select(Path.GetFileName).Where(n => n != null).Cast<string>();
        var versions = GetVersionFolders(folderNames);

        foreach (var v in versions)
        {
            var leaf = new LocalLeafFolder(Path.Combine(versionsPath, v.FolderName));
            var metadata = leaf.GetMetadata();
            if (metadata.TryGetValue(AppConstants.Metadata.VersioningParents, out var parents))
            {
                v.ParentVersions = parents.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            }
        }

        return versions;
    }

    internal List<VersionFolderInfo> GetVersionFolders(IEnumerable<string> folderNames)
    {
        return folderNames
            .Select(name => ParseVersionFolder(name!))
            .Where(info => info != null)
            .Cast<VersionFolderInfo>()
            .OrderBy(v => v.Sequence)
            .ToList();
    }

    internal VersionFolderInfo? ParseVersionFolder(string folderName)
    {
        var match = VersionRegex.Match(folderName);
        if (!match.Success) return null;

        return new VersionFolderInfo
        {
            FolderName = folderName,
            Sequence = int.Parse(match.Groups["nnn"].Value),
            Kind = Enum.Parse<CommitKind>(match.Groups["kind"].Value, true),
            DeviceId = match.Groups["deviceId"].Value
        };
    }

    public Dictionary<string, string> GetMetadata()
    {
        var pointerFile = Path.Combine(_rootPath, AppConstants.Files.TripFundFile);
        if (!File.Exists(pointerFile)) return new Dictionary<string, string>();

        return File.ReadAllLines(pointerFile)
            .Select(l => l.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
    }

    public string? ResolveHeadPath()
    {
        var meta = GetMetadata();
        if (meta.TryGetValue(AppConstants.Metadata.VersioningHead, out var head) && !string.IsNullOrEmpty(head))
        {
            var headPath = Path.Combine(_rootPath, AppConstants.Folders.Versions, head);
            return Directory.Exists(headPath) ? headPath : null;
        }

        return null;
    }

    public bool HasConflicts()
    {
        var meta = GetMetadata();
        return meta.TryGetValue(AppConstants.Metadata.VersioningConflict, out var conflict) && !string.IsNullOrEmpty(conflict);
    }

    public List<string> GetConflictFolderNames()
    {
        var meta = GetMetadata();
        if (meta.TryGetValue(AppConstants.Metadata.VersioningConflict, out var conflict) && !string.IsNullOrEmpty(conflict))
        {
            return conflict.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        return new List<string>();
    }

    public async Task UpdateHeadAsync()
    {
        var leaves = GetLatestVersionFolders();
        if (leaves.Count == 0) return;

        // Selection logic for versioning.head:
        // 1. A leaf created by current deviceId.
        // 2. The leaf with highest sequence.
        var head = leaves.FirstOrDefault(l => l.DeviceId == _deviceId) ?? leaves.OrderByDescending(l => l.Sequence).First();

        // Logic for versioning.conflict:
        // CSV of all leaf folder names if count > 1.
        var conflict = leaves.Count > 1 ? string.Join(",", leaves.Select(l => l.FolderName)) : string.Empty;

        var pointerFile = Path.Combine(_rootPath, AppConstants.Files.TripFundFile);
        var meta = new Dictionary<string, string>
        {
            [AppConstants.Metadata.ContentType] = AppConstants.ContentTypes.VersionedStorage,
            [AppConstants.Metadata.VersioningHead] = head.FolderName,
            [AppConstants.Metadata.VersioningConflict] = conflict
        };

        var lines = meta.Select(kvp => $"{kvp.Key}={kvp.Value}");
        await File.WriteAllLinesAsync(pointerFile, lines);
    }

    public List<VersionFolderInfo> GetLatestVersionFolders()
    {
        var versions = GetVersionFolders();
        return GetLatestVersionFolders(versions);
    }

    internal List<VersionFolderInfo> GetLatestVersionFolders(List<VersionFolderInfo> versions)
    {
        if (versions.Count == 0) return new List<VersionFolderInfo>();

        var leaves = new List<VersionFolderInfo>();
        foreach (var v in versions)
        {
            bool isSuperseded = false;
            foreach (var other in versions)
            {
                if (other.FolderName == v.FolderName) continue;

                if (Supersedes(other, v, versions))
                {
                    isSuperseded = true;
                    break;
                }
            }
            if (!isSuperseded)
            {
                leaves.Add(v);
            }
        }

        return leaves.OrderBy(v => v.Sequence).ToList();
    }

    private bool Supersedes(VersionFolderInfo A, VersionFolderInfo B, List<VersionFolderInfo> allVersions)
    {
        // Folder A supersedes B if B is an ancestor of A.
        return IsAncestor(B.FolderName, A, allVersions);
    }

    private bool IsAncestor(string targetFolderName, VersionFolderInfo current, List<VersionFolderInfo> allVersions)
    {
        if (current.ParentVersions == null || current.ParentVersions.Count == 0) return false;

        foreach (var parentName in current.ParentVersions)
        {
            if (parentName == targetFolderName) return true;

            var parentInfo = allVersions.FirstOrDefault(v => v.FolderName == parentName);
            if (parentInfo != null && IsAncestor(targetFolderName, parentInfo, allVersions))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsNew()
    {
        var meta = GetMetadata();
        return !meta.ContainsKey(AppConstants.Metadata.VersioningHead);
    }

    public string? GetConflictBaseFolder()
    {
        var conflictNames = GetConflictFolderNames();
        if (conflictNames.Count <= 1) return null;

        var allVersions = GetVersionFolders();
        var leaves = allVersions.Where(v => conflictNames.Contains(v.FolderName)).ToList();
        return GetBaseVersionFolder(leaves, allVersions);
    }

    public List<VersionFolderInfo> GetConflictingFoldersInfo()
    {
        var conflictNames = GetConflictFolderNames();
        return GetVersionFolders(conflictNames);
    }

    internal string? GetBaseVersionFolder(List<VersionFolderInfo> leaves, List<VersionFolderInfo> allVersions)
    {
        if (leaves.Count <= 1) return null;

        // 1. Get all ancestors for each leaf (including the leaf itself)
        var ancestorSets = leaves.Select(leaf => GetAllAncestors(leaf, allVersions)).ToList();

        // 2. Find intersection (folders that are ancestors of ALL leaves)
        var commonAncestors = ancestorSets[0];
        for (int i = 1; i < ancestorSets.Count; i++)
        {
            commonAncestors.IntersectWith(ancestorSets[i]);
        }

        // 3. Remove the leaves themselves from common ancestors if they are part of it 
        // (an LCA must be a strict ancestor if we have multiple diverging leaves)
        foreach (var leaf in leaves)
        {
            commonAncestors.Remove(leaf.FolderName);
        }

        if (commonAncestors.Count == 0) return null;

        // 4. Pick the one with the highest sequence number
        return allVersions
            .Where(v => commonAncestors.Contains(v.FolderName))
            .OrderByDescending(v => v.Sequence)
            .Select(v => v.FolderName)
            .FirstOrDefault();
    }

    private HashSet<string> GetAllAncestors(VersionFolderInfo folder, List<VersionFolderInfo> allVersions)
    {
        var ancestors = new HashSet<string> { folder.FolderName };
        var queue = new Queue<string>(folder.ParentVersions);

        while (queue.Count > 0)
        {
            var parentName = queue.Dequeue();
            if (ancestors.Add(parentName))
            {
                var parentInfo = allVersions.FirstOrDefault(v => v.FolderName == parentName);
                if (parentInfo != null)
                {
                    foreach (var p in parentInfo.ParentVersions) queue.Enqueue(p);
                }
            }
        }

        return ancestors;
    }

    public async Task<string> CommitAsync(
        CommitKind kind,
        Dictionary<string, byte[]> changedFiles,
        List<string>? deletedFiles = null,
        IEnumerable<string>? parentVersions = null,
        string? contentType = null,
        string? tempRootPath = null)
    {
        var versions = GetVersionFolders();
        int nextSeq = (versions.Count == 0) ? 1 : versions.Max(v => v.Sequence) + 1;
        string folderName = $"{nextSeq:D3}_{kind.ToString().ToUpperInvariant()}_{_deviceId}";
        string versionsRoot = Path.Combine(_rootPath, AppConstants.Folders.Versions);

        string workDirPath = string.IsNullOrEmpty(tempRootPath)
            ? Path.Combine(versionsRoot, folderName)
            : Path.Combine(tempRootPath, folderName);

        if (!Directory.Exists(Path.GetDirectoryName(workDirPath))) 
            Directory.CreateDirectory(Path.GetDirectoryName(workDirPath)!);

        if (Directory.Exists(workDirPath)) Directory.Delete(workDirPath, true);
        Directory.CreateDirectory(workDirPath);

        var leaf = new LocalLeafFolder(workDirPath);
        var metaDict = new Dictionary<string, string>();

        metaDict[AppConstants.Metadata.Author] = _author;
        metaDict[AppConstants.Metadata.DeviceId] = _deviceId;
        metaDict[AppConstants.Metadata.CreatedAt] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        if (!string.IsNullOrEmpty(contentType)) metaDict[AppConstants.Metadata.ContentType] = contentType;

        if (parentVersions != null && parentVersions.Any())
        {
            metaDict[AppConstants.Metadata.VersioningParents] = string.Join(",", parentVersions);
        }

        metaDict[AppConstants.Metadata.VersioningSequence] = nextSeq.ToString();
        metaDict[AppConstants.Metadata.VersioningKind] = kind.ToString().ToUpperInvariant();

        await leaf.SaveMetadataAsync(metaDict);

        if (kind != CommitKind.Del)
        {
            await leaf.EnsureDataDirectoryAsync();

            // For 'upd' and 'res', we need to copy previous state
            if (kind == CommitKind.Upd || kind == CommitKind.Res)
            {
                // We prioritize the first provided parent as the source for copying
                string? sourceFolderName = parentVersions?.FirstOrDefault();
                
                if (string.IsNullOrEmpty(sourceFolderName))
                {
                    // Fallback to latest sequence if no parents provided (should not happen in new system)
                    var previousVersions = versions.Where(v => v.Sequence < nextSeq).ToList();
                    if (previousVersions.Any())
                    {
                        var maxPrevSeq = previousVersions.Max(v => v.Sequence);
                        var latestOfPrev = previousVersions.Where(v => v.Sequence == maxPrevSeq).ToList();
                        var resOfPrev = latestOfPrev.Where(v => v.Kind == CommitKind.Res).ToList();
                        sourceFolderName = resOfPrev.Any() ? resOfPrev.First().FolderName : latestOfPrev.First().FolderName;
                    }
                }

                if (!string.IsNullOrEmpty(sourceFolderName))
                {
                    var prevLeaf = new LocalLeafFolder(Path.Combine(versionsRoot, sourceFolderName));

                    if (!(await prevLeaf.IsDataEmptyAsync()))
                    {
                        var prevFiles = await prevLeaf.ListDataFilesAsync();
                        foreach (var fileName in prevFiles)
                        {
                            if (deletedFiles != null && deletedFiles.Contains(fileName)) continue;
                            if (changedFiles.ContainsKey(fileName)) continue;

                            var content = await prevLeaf.ReadDataFileAsync(fileName);
                            await leaf.WriteDataFileAsync(fileName, content);
                        }
                    }
                }
            }

            foreach (var file in changedFiles)
            {
                await leaf.WriteDataFileAsync(file.Key, file.Value);
            }
        }

        if (!string.IsNullOrEmpty(tempRootPath))
        {
            string finalPath = Path.Combine(versionsRoot, folderName);
            if (!Directory.Exists(versionsRoot)) Directory.CreateDirectory(versionsRoot);
            if (Directory.Exists(finalPath)) Directory.Delete(finalPath, true);
            Directory.Move(workDirPath, finalPath);
        }

        await UpdateHeadAsync();

        return folderName;
    }
}
