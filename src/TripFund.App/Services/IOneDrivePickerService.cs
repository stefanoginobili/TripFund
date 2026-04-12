using System.Threading.Tasks;

namespace TripFund.App.Services;

public interface IOneDrivePickerService
{
    Task<(string? FolderId, string? FolderName)> PickFolderAsync(string accessToken, string title);
}
