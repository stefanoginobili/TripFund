using TripFund.App.Models;

namespace TripFund.App.Services;

public interface IRemoteStorageService
{
    Task<TripConfig?> GetRemoteTripConfigAsync(string provider, Dictionary<string, string> parameters);
    Task<bool> IsRemoteLocationEmptyAsync(string provider, Dictionary<string, string> parameters);
    Task SynchronizeAsync(string tripSlug);
}

public class RemoteStorageFolder
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
