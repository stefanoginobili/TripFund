using System.Text.RegularExpressions;

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
    public IReadOnlyList<string>? ResolvedFolders { get; set; }
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

    public static readonly IReadOnlyList<Regex> IgnoredSystemFiles = new List<Regex>
    {
        new(@"^\.synched\.tf$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"^\.synching\.tf$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"^\.resolved_versions\.tf$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"^\.deleted\.tf$", RegexOptions.Compiled | RegexOptions.IgnoreCase)
    };

    public List<VersionFolderInfo> GetVersionFolders(string rootPath)
    {
        if (!Directory.Exists(rootPath)) return new List<VersionFolderInfo>();

        var folderNames = Directory.GetDirectories(rootPath).Select(Path.GetFileName).Where(n => n != null).Cast<string>();
        var versions = GetVersionFolders(folderNames);

        foreach (var v in versions)
        {
            if (v.Kind == CommitKind.Res)
            {
                var resolvesFile = Path.Combine(rootPath, v.FolderName, ".resolved_versions.tf");
                if (File.Exists(resolvesFile))
                {
                    v.ResolvedFolders = File.ReadAllLines(resolvesFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                }
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
        // 1. Device-Local Progression (Highest sequence for same device wins)
        if (A.DeviceId == B.DeviceId && A.Sequence > B.Sequence)
        {
            return true;
        }

        // 2. Global Linear Progression (Sequence N+1 wins over N if N was unique)
        if (A.Sequence == B.Sequence + 1)
        {
            int countAtBSeq = allVersions.Count(v => v.Sequence == B.Sequence);
            if (countAtBSeq == 1) return true;
        }

        // 3. Explicit Resolution (RES wins over the exact folders it resolves)
        if (A.Kind == CommitKind.Res && A.ResolvedFolders != null && A.ResolvedFolders.Contains(B.FolderName))
        {
            return true;
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
        // Keep this signature for backward compatibility or simple cases
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
        if (latest.Count <= 1) return false;

        // If we have multiple folders with same sequence, it's a conflict
        // unless they are reconciled by a later 'res' (but GetLatestVersionFolders already handles that)
        return true;
    }

    public async Task<string> CommitAsync(
        string rootPath, 
        string deviceId, 
        CommitKind kind, 
        Dictionary<string, byte[]> changedFiles, 
        List<string>? deletedFiles = null,
        string? deletedInfo = null,
        IEnumerable<string>? resolvedFolders = null)
    {
        var versions = GetVersionFolders(rootPath);
        int nextSeq = (versions.Count == 0) ? 1 : versions.Max(v => v.Sequence) + 1;
        string folderName = $"{nextSeq:D3}_{kind.ToString().ToUpperInvariant()}_{deviceId}";
        string newDirPath = Path.Combine(rootPath, folderName);
        Directory.CreateDirectory(newDirPath);

        if (kind == CommitKind.Res && resolvedFolders != null)
        {
            await File.WriteAllLinesAsync(Path.Combine(newDirPath, ".resolved_versions.tf"), resolvedFolders);
        }

        if (kind == CommitKind.Del)
        {
            await File.WriteAllTextAsync(Path.Combine(newDirPath, ".deleted.tf"), deletedInfo ?? "");
            return folderName;
        }

        // For 'upd' and 'res', we need to copy previous state
        if (kind == CommitKind.Upd || kind == CommitKind.Res)
        {
            // We need to find the latest version BEFORE nextSeq
            var previousVersions = versions.Where(v => v.Sequence < nextSeq).ToList();
            if (previousVersions.Any())
            {
                var maxPrevSeq = previousVersions.Max(v => v.Sequence);
                var latestOfPrev = previousVersions.Where(v => v.Sequence == maxPrevSeq).ToList();
                
                // Prioritize 'res' if it exists at the previous level
                var resOfPrev = latestOfPrev.Where(v => v.Kind == CommitKind.Res).ToList();
                var sourceInfo = resOfPrev.Any() ? resOfPrev.First() : latestOfPrev.First();

                var prevPath = Path.Combine(rootPath, sourceInfo.FolderName);
                
                if (!File.Exists(Path.Combine(prevPath, ".deleted.tf")))
                {
                    foreach (var file in Directory.GetFiles(prevPath))
                    {
                        var fileName = Path.GetFileName(file);
                        if (deletedFiles != null && deletedFiles.Contains(fileName)) continue;
                        if (changedFiles.ContainsKey(fileName)) continue;
                        if (IgnoredSystemFiles.Any(regex => regex.IsMatch(fileName))) continue;
                        
                        File.Copy(file, Path.Combine(newDirPath, fileName));
                    }
                }
            }
        }

        foreach (var file in changedFiles)
        {
            await File.WriteAllBytesAsync(Path.Combine(newDirPath, file.Key), file.Value);
        }

        return folderName;
    }
}
