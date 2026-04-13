namespace TripFund.App.Models;

public class RemoteItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public bool IsRemote { get; set; }
    public string? ETag { get; set; }
    public string? DownloadUrl { get; set; }
}
