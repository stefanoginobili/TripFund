namespace TripFund.App.Services;

public interface IDriveService
{
    Task<DriveFolder?> PickFolderAsync();
}

public class DriveFolder
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
