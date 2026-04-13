using TripFund.App.Models;

namespace TripFund.App.Services;

public interface IRemoteFileSystem
{
    IRemoteStorageLogger? Logger { get; set; }
    Task<List<RemoteItem>> ListChildrenAsync(string folderId, Dictionary<string, string> parameters);
    Task<RemoteItem?> GetChildItemAsync(string parentId, string name, Dictionary<string, string> parameters);
    Task<byte[]?> DownloadFileContentAsync(string fileId, Dictionary<string, string> parameters);
    Task<RemoteItem?> CreateFolderAsync(string parentId, string name, Dictionary<string, string> parameters);
    Task<RemoteItem?> UploadFileAsync(string parentId, string name, byte[] content, Dictionary<string, string> parameters);
    Task DeleteFileAsync(string fileId, Dictionary<string, string> parameters);
    Task EnsureAuthenticatedAsync(Dictionary<string, string> parameters);
}
