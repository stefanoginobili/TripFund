using TripFund.App.Models;

namespace TripFund.App.Services;

public class GitSyncService : ISyncService
{

    public Task<TripConfig?> GetRemoteTripConfigAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider == "git" && parameters.TryGetValue("repository", out var repo))
        {
            if (repo.Contains("existing"))
            {
                return Task.FromResult<TripConfig?>(new TripConfig
                {
                    Id = "existing-git-trip",
                    Name = "Viaggio Esistente Git",
                    StartDate = DateTime.Today.AddDays(30),
                    EndDate = DateTime.Today.AddDays(40)
                });
            }
        }

        return Task.FromResult<TripConfig?>(null);
    }

    public Task<bool> IsRemoteLocationEmptyAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider == "git" && parameters.TryGetValue("repository", out var repo))
        {
            // Simulate empty only if URL contains "empty"
            return Task.FromResult(repo.Contains("empty"));
        }
        return Task.FromResult(false);
    }
}
