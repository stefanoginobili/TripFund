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
}

public class VersionConflictException : Exception
{
    public List<string> ConflictingFolderNames { get; }

    public VersionConflictException(List<string> conflictingFolderNames)
        : base($"Conflict detected between versions: {string.Join(", ", conflictingFolderNames)}")
    {
        ConflictingFolderNames = conflictingFolderNames;
    }
}

public class VersionedStorageEngine
{
    private static readonly Regex VersionRegex = new(@"^(?<nnn>\d{3})_(?<kind>NEW|UPD|RES|DEL|new|upd|res|del)_(?<deviceId>[a-z0-9\-]+)$", RegexOptions.Compiled);

    public List<VersionFolderInfo> GetVersionFolders(string rootPath)
    {
        if (!Directory.Exists(rootPath)) return new List<VersionFolderInfo>();

        return GetVersionFolders(Directory.GetDirectories(rootPath).Select(Path.GetFileName)!);
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

        var maxSequence = versions.Max(v => v.Sequence);
        var latestAtSequence = versions.Where(v => v.Sequence == maxSequence).ToList();

        // If there's a 'res' folder at the latest sequence, it wins over 'upd' or 'new'
        var resVersions = latestAtSequence.Where(v => v.Kind == CommitKind.Res).ToList();
        if (resVersions.Count > 0) return resVersions;

        return latestAtSequence;
    }

    public string? GetLatestVersionFolder(IEnumerable<string> folderNames)
    {
        var versions = GetVersionFolders(folderNames);
        var latest = GetLatestVersionFolders(versions);

        if (latest.Count == 0) return null;
        if (latest.Count > 1)
        {
            throw new VersionConflictException(latest.Select(v => v.FolderName).ToList());
        }

        return latest[0].FolderName;
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
        string? deletedInfo = null)
    {
        var versions = GetVersionFolders(rootPath);
        int nextSeq = (versions.Count == 0) ? 1 : versions.Max(v => v.Sequence) + 1;
        string folderName = $"{nextSeq:D3}_{kind.ToString().ToUpperInvariant()}_{deviceId}";
        string newDirPath = Path.Combine(rootPath, folderName);
        Directory.CreateDirectory(newDirPath);

        if (kind == CommitKind.Del)
        {
            await File.WriteAllTextAsync(Path.Combine(newDirPath, ".deleted"), deletedInfo ?? "");
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
                
                if (!File.Exists(Path.Combine(prevPath, ".deleted")))
                {
                    foreach (var file in Directory.GetFiles(prevPath))
                    {
                        var fileName = Path.GetFileName(file);
                        if (deletedFiles != null && deletedFiles.Contains(fileName)) continue;
                        if (changedFiles.ContainsKey(fileName)) continue;
                        
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
