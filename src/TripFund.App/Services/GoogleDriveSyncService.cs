using TripFund.App.Models;

namespace TripFund.App.Services;

public class GoogleDriveSyncService : ISyncService
{

    public Task<TripConfig?> GetRemoteTripConfigAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider == "google-drive" && parameters.TryGetValue("folderUrl", out var url))
        {
            if (url.Contains("existing"))
            {
                return Task.FromResult<TripConfig?>(new TripConfig
                {
                    Id = "existing-drive-trip",
                    Name = "Viaggio Esistente Drive",
                    StartDate = DateTime.Today.AddDays(10),
                    EndDate = DateTime.Today.AddDays(20)
                });
            }
        }

        return Task.FromResult<TripConfig?>(null);
    }

    public Task<bool> IsRemoteLocationEmptyAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider == "google-drive" && parameters.TryGetValue("folderUrl", out var url))
        {
            // Simulate empty only if URL contains "empty"
            return Task.FromResult(url.Contains("empty"));
        }
        return Task.FromResult(false);
    }
}
