using System.Collections.Generic;
using System.Threading.Tasks;

namespace TripFund.App.Services;

public interface IOneDrivePickerService
{
    Task<(string? FolderId, string? FolderName)> PickFolderAsync(string accessToken, string title);
    Task<List<OneDriveItem>> ListFoldersAsync(string accessToken, string? parentFolderId = null, string? driveId = null);
    Task<List<OneDriveItem>> ListSharedFoldersAsync(string accessToken);
    Task<OneDriveItem> CreateFolderAsync(string accessToken, string folderName, string? parentFolderId = null, string? driveId = null);
}
