using TripFund.App.Models;

namespace TripFund.App.Services;

public class CompositeSyncService : ISyncService
{
    private readonly GoogleDriveSyncService _drive;
    private readonly GitSyncService _git;

    public CompositeSyncService(GoogleDriveSyncService drive, GitSyncService git)
    {
        _drive = drive;
        _git = git;
    }

    public Task<TripConfig?> GetRemoteTripConfigAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider == "google-drive")
        {
            return _drive.GetRemoteTripConfigAsync(provider, parameters);
        }
        else if (provider == "git")
        {
            return _git.GetRemoteTripConfigAsync(provider, parameters);
        }

        return Task.FromResult<TripConfig?>(null);
    }

    public Task<bool> IsRemoteLocationEmptyAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider == "google-drive")
        {
            return _drive.IsRemoteLocationEmptyAsync(provider, parameters);
        }
        else if (provider == "git")
        {
            return _git.IsRemoteLocationEmptyAsync(provider, parameters);
        }

        return Task.FromResult(false);
    }
}
