using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TripFund.App.Services;
using TripFund.App.Constants;

namespace TripFund.App.Models;

public class RemoteLeafFolder : LeafFolder
{
    private readonly IRemoteFileSystem _fileSystem;
    private readonly string _folderId;
    private readonly Dictionary<string, string> _parameters;
    
    private const string ContentFolderName = AppConstants.Folders.Content;
    private const string TripFundFileName = AppConstants.Files.TripFundFile;

    public RemoteLeafFolder(IRemoteFileSystem fileSystem, string folderId, Dictionary<string, string> parameters)
    {
        _fileSystem = fileSystem;
        _folderId = folderId;
        _parameters = parameters;
    }

    public override async Task<Dictionary<string, string>> GetMetadataAsync()
    {
        var result = new Dictionary<string, string>();
        var metadataItem = await _fileSystem.GetChildItemAsync(_folderId, TripFundFileName, _parameters);
        if (metadataItem != null)
        {
            var content = await _fileSystem.DownloadFileContentAsync(metadataItem.Id, _parameters);
            if (content != null)
            {
                var text = Encoding.UTF8.GetString(content);
                var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        result[parts[0]] = parts[1];
                    }
                }
            }
        }
        return result;
    }

    public override async Task SaveMetadataAsync(Dictionary<string, string> metadata)
    {
        var existing = await _fileSystem.GetChildItemAsync(_folderId, TripFundFileName, _parameters);
        if (existing != null)
        {
            await _fileSystem.DeleteFileAsync(existing.Id, _parameters);
        }
        var text = string.Join("\n", metadata.Select(kv => $"{kv.Key}={kv.Value}"));
        var content = Encoding.UTF8.GetBytes(text);
        await _fileSystem.UploadFileAsync(_folderId, TripFundFileName, content, _parameters);
    }

    public override async Task<bool> IsDataEmptyAsync()
    {
        var dataFolder = await _fileSystem.GetChildItemAsync(_folderId, ContentFolderName, _parameters);
        if (dataFolder == null || !dataFolder.IsFolder) return true;
        var children = await _fileSystem.ListChildrenAsync(dataFolder.Id, _parameters);
        return !children.Any();
    }

    public override async Task EnsureDataDirectoryAsync()
    {
        var dataFolder = await _fileSystem.GetChildItemAsync(_folderId, ContentFolderName, _parameters);
        if (dataFolder == null)
        {
            await _fileSystem.CreateFolderAsync(_folderId, ContentFolderName, _parameters);
        }
    }

    public override async Task<List<string>> ListDataFilesAsync()
    {
        var dataFolder = await _fileSystem.GetChildItemAsync(_folderId, ContentFolderName, _parameters);
        if (dataFolder == null || !dataFolder.IsFolder) return new List<string>();
        var children = await _fileSystem.ListChildrenAsync(dataFolder.Id, _parameters);
        return children.Where(c => !c.IsFolder).Select(c => c.Name).ToList();
    }

    public override async Task<byte[]> ReadDataFileAsync(string fileName)
    {
        var dataFolder = await _fileSystem.GetChildItemAsync(_folderId, ContentFolderName, _parameters);
        if (dataFolder == null) throw new FileNotFoundException("Data folder not found.");
        var file = await _fileSystem.GetChildItemAsync(dataFolder.Id, fileName, _parameters);
        if (file == null) throw new FileNotFoundException($"File {fileName} not found in data folder.");
        return await _fileSystem.DownloadFileContentAsync(file.Id, _parameters) ?? Array.Empty<byte>();
    }

    public override async Task WriteDataFileAsync(string fileName, byte[] content)
    {
        var dataFolder = await _fileSystem.GetChildItemAsync(_folderId, ContentFolderName, _parameters);
        if (dataFolder == null)
        {
            dataFolder = await _fileSystem.CreateFolderAsync(_folderId, ContentFolderName, _parameters);
        }
        if (dataFolder == null) throw new InvalidOperationException("Failed to create/get data folder.");

        var existing = await _fileSystem.GetChildItemAsync(dataFolder.Id, fileName, _parameters);
        if (existing != null)
        {
            await _fileSystem.DeleteFileAsync(existing.Id, _parameters);
        }
        await _fileSystem.UploadFileAsync(dataFolder.Id, fileName, content, _parameters);
    }

    public override async Task DeleteDataFileAsync(string fileName)
    {
        var dataFolder = await _fileSystem.GetChildItemAsync(_folderId, ContentFolderName, _parameters);
        if (dataFolder != null)
        {
            var file = await _fileSystem.GetChildItemAsync(dataFolder.Id, fileName, _parameters);
            if (file != null)
            {
                await _fileSystem.DeleteFileAsync(file.Id, _parameters);
            }
        }
    }

    public override async Task<bool> HasMarkerAsync(string markerName)
    {
        var marker = await _fileSystem.GetChildItemAsync(_folderId, markerName, _parameters);
        return marker != null;
    }

    public override async Task WriteMarkerAsync(string markerName, string? content = null)
    {
        var existing = await _fileSystem.GetChildItemAsync(_folderId, markerName, _parameters);
        if (existing != null)
        {
            await _fileSystem.DeleteFileAsync(existing.Id, _parameters);
        }
        var bytes = Encoding.UTF8.GetBytes(content ?? "");
        await _fileSystem.UploadFileAsync(_folderId, markerName, bytes, _parameters);
    }

    public override async Task DeleteMarkerAsync(string markerName)
    {
        var marker = await _fileSystem.GetChildItemAsync(_folderId, markerName, _parameters);
        if (marker != null)
        {
            await _fileSystem.DeleteFileAsync(marker.Id, _parameters);
        }
    }
}
