using System.Text.Json.Serialization;

namespace TripFund.App.Models;

public class SyncState
{
    [JsonPropertyName("sync")]
    public SyncData Sync { get; set; } = new();
}

public class SyncData
{
    [JsonPropertyName("remote")]
    public RemoteSyncData Remote { get; set; } = new();

    [JsonPropertyName("local")]
    public LocalSyncData Local { get; set; } = new();
}

public class RemoteSyncData
{
    [JsonPropertyName("appliedPackages")]
    public List<string> AppliedPackages { get; set; } = new();
}

public class LocalSyncData
{
    [JsonPropertyName("pending")]
    public List<PendingUpload> Pending { get; set; } = new();
}

public class PendingUpload
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;
}
