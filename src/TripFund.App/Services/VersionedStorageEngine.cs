using System.Text.RegularExpressions;
using TripFund.App.Models;
using TripFund.App.Constants;

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

    public List<VersionFolderInfo> GetVersionFolders(string rootPath)
    {
        if (!Directory.Exists(rootPath)) return new List<VersionFolderInfo>();

        var folderNames = Directory.GetDirectories(rootPath).Select(Path.GetFileName).Where(n => n != null).Cast<string>();
        var versions = GetVersionFolders(folderNames);

        foreach (var v in versions)
        {
            var leaf = new LocalLeafFolder(Path.Combine(rootPath, v.FolderName));
            var metadata = leaf.GetMetadata();
            if (metadata.TryGetValue(AppConstants.Metadata.VersioningParents, out var parents))
            {
                v.ParentVersions = parents.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            }
        }

        return versions;
    }

    public List<VersionFolderInfo> GetVersionFolders(IEnumerable<string> folderNames)
    {
        return folderNames
            .Select(name => ParseVersionFolder(name!))
            .Where(info => info != null)
            .Cast<VersionFolderInfo>()
            .OrderBy(v => v.Sequence)
            .ToList();
    }

    public VersionFolderInfo? ParseVersionFolder(string folderName)
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

    public List<VersionFolderInfo> GetLatestVersionFolders(string rootPath)
    {
        var versions = GetVersionFolders(rootPath);
        return GetLatestVersionFolders(versions);
    }

    public List<VersionFolderInfo> GetLatestVersionFolders(List<VersionFolderInfo> versions)
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

    public string? GetLatestVersionFolder(string rootPath)
    {
        var versions = GetVersionFolders(rootPath);
        var latest = GetLatestVersionFolders(versions);

        if (latest.Count == 0) return null;
        if (latest.Count > 1)
        {
            var diverging = latest.Select(v => v.FolderName).ToList();
            var baseVer = GetBaseVersionFolder(rootPath, latest);
            throw new VersionedFolderConflictException($"Conflict detected between versions: {string.Join(", ", diverging)}", diverging, baseVer);
        }

        return latest[0].FolderName;
    }

    public string? GetLatestVersionFolder(IEnumerable<string> folderNames)
    {
        var versions = GetVersionFolders(folderNames);
        var latest = GetLatestVersionFolders(versions);

        if (latest.Count == 0) return null;
        if (latest.Count > 1)
        {
            var diverging = latest.Select(v => v.FolderName).ToList();
            throw new VersionedFolderConflictException($"Conflict detected between versions: {string.Join(", ", diverging)}", diverging, null);
        }

        return latest[0].FolderName;
    }

    public string? GetBaseVersionFolder(string rootPath, List<VersionFolderInfo> leaves)
    {
        if (leaves.Count <= 1) return null;

        var versions = GetVersionFolders(rootPath);
        int minLeafSeq = leaves.Min(l => l.Sequence);

        for (int s = minLeafSeq - 1; s >= 1; s--)
        {
            var atSeq = versions.Where(v => v.Sequence == s).ToList();
            if (atSeq.Count == 1) return atSeq[0].FolderName;
        }

        return null;
    }

    public string? GetBaseVersionFolder(string rootPath, int conflictSequence)
    {
        var versions = GetVersionFolders(rootPath);
        return versions
            .Where(v => v.Sequence < conflictSequence)
            .OrderByDescending(v => v.Sequence)
            .Select(v => v.FolderName)
            .FirstOrDefault();
    }

    public List<string> GetDivergingVersionFolders(IEnumerable<string> folderNames)
    {
        var versions = GetVersionFolders(folderNames);
        var latest = GetLatestVersionFolders(versions);
        
        if (latest.Count <= 1) return new List<string>();
        
        return latest.Select(v => v.FolderName).ToList();
    }

    public bool IsInConflict(string rootPath)
    {
        var latest = GetLatestVersionFolders(rootPath);
        return latest.Count > 1;
    }

    public async Task<string> CommitAsync(
        string rootPath,
        string deviceId,
        CommitKind kind,
        Dictionary<string, byte[]> changedFiles,
        List<string>? deletedFiles = null,
        Dictionary<string, string>? metadata = null,
        IEnumerable<string>? parentVersions = null,
        string? contentType = null,
        string? tempRootPath = null)
    {
        var versions = GetVersionFolders(rootPath);
        int nextSeq = (versions.Count == 0) ? 1 : versions.Max(v => v.Sequence) + 1;
        string folderName = $"{nextSeq:D3}_{kind.ToString().ToUpperInvariant()}_{deviceId}";

        string workDirPath = string.IsNullOrEmpty(tempRootPath)
            ? Path.Combine(rootPath, folderName)
            : Path.Combine(tempRootPath, folderName);

        if (Directory.Exists(workDirPath)) Directory.Delete(workDirPath, true);
        Directory.CreateDirectory(workDirPath);

        var leaf = new LocalLeafFolder(workDirPath);
        var metaDict = metadata != null ? new Dictionary<string, string>(metadata) : new Dictionary<string, string>();

        if (!metaDict.ContainsKey(AppConstants.Metadata.Author)) metaDict[AppConstants.Metadata.Author] = "unknown";
        if (!metaDict.ContainsKey(AppConstants.Metadata.DeviceId)) metaDict[AppConstants.Metadata.DeviceId] = deviceId;
        if (!metaDict.ContainsKey(AppConstants.Metadata.CreatedAt)) metaDict[AppConstants.Metadata.CreatedAt] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        if (!string.IsNullOrEmpty(contentType)) metaDict[AppConstants.Metadata.ContentType] = contentType;

        if (parentVersions != null && parentVersions.Any())
        {
            metaDict[AppConstants.Metadata.VersioningParents] = string.Join(",", parentVersions);
        }

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
                    var prevLeaf = new LocalLeafFolder(Path.Combine(rootPath, sourceFolderName));

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
            string finalPath = Path.Combine(rootPath, folderName);
            if (Directory.Exists(finalPath)) Directory.Delete(finalPath, true);
            Directory.Move(workDirPath, finalPath);
        }

        return folderName;
    }
}
