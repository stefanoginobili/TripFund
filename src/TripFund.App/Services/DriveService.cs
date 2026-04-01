namespace TripFund.App.Services;

public class MockDriveService : IDriveService
{
    public Task<DriveFolder?> PickFolderAsync()
    {
        // Simple mock: return a dummy folder
        return Task.FromResult<DriveFolder?>(new DriveFolder
        {
            Id = "mock-drive-folder-id-" + Guid.NewGuid().ToString().Substring(0, 8),
            Name = "Mock Drive Folder"
        });
    }
}
