using TripFund.App.Models;

namespace TripFund.App.Services;

public interface ISyncService
{
    Task<TripConfig?> GetRemoteTripConfigAsync(string provider, Dictionary<string, string> parameters);
    Task<bool> IsRemoteLocationEmptyAsync(string provider, Dictionary<string, string> parameters);
}

public class SyncFolder
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
