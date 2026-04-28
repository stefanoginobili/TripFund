using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TripFund.App.Constants;

namespace TripFund.App.Models;

public class LocalLeafFolder : LeafFolder
{
    private readonly string _path;
    private const string ContentFolderName = AppConstants.Folders.ContentFolder;
    private const string TripFundFileName = AppConstants.Files.TripFundFile;

    public LocalLeafFolder(string path)
    {
        _path = path;
    }

    public override async Task<Dictionary<string, string>> GetMetadataAsync()
    {
        var metadataPath = Path.Combine(_path, TripFundFileName);
        var result = new Dictionary<string, string>();
        if (File.Exists(metadataPath))
        {
            var lines = await File.ReadAllLinesAsync(metadataPath);
            foreach (var line in lines)
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    result[parts[0]] = parts[1];
                }
            }
        }
        return result;
    }

    public override Dictionary<string, string> GetMetadata()
    {
        var metadataPath = Path.Combine(_path, TripFundFileName);
        var result = new Dictionary<string, string>();
        if (File.Exists(metadataPath))
        {
            var lines = File.ReadAllLines(metadataPath);
            foreach (var line in lines)
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    result[parts[0]] = parts[1];
                }
            }
        }
        return result;
    }

    public override async Task SaveMetadataAsync(Dictionary<string, string> metadata)
    {
        if (!Directory.Exists(_path)) Directory.CreateDirectory(_path);
        var metadataPath = Path.Combine(_path, TripFundFileName);
        var lines = metadata.Select(kv => $"{kv.Key}={kv.Value}");
        await File.WriteAllLinesAsync(metadataPath, lines);
    }

    public override void SaveMetadata(Dictionary<string, string> metadata)
    {
        if (!Directory.Exists(_path)) Directory.CreateDirectory(_path);
        var metadataPath = Path.Combine(_path, TripFundFileName);
        var lines = metadata.Select(kv => $"{kv.Key}={kv.Value}");
        File.WriteAllLines(metadataPath, lines);
    }

    public override Task<bool> IsDataEmptyAsync()
    {
        var dataPath = Path.Combine(_path, ContentFolderName);
        if (!Directory.Exists(dataPath)) return Task.FromResult(true);
        return Task.FromResult(!Directory.EnumerateFileSystemEntries(dataPath).Any());
    }

    public override Task EnsureDataDirectoryAsync()
    {
        EnsureDataDirectory();
        return Task.CompletedTask;
    }

    public override void EnsureDataDirectory()
    {
        var dataPath = Path.Combine(_path, ContentFolderName);
        if (!Directory.Exists(dataPath))
        {
            Directory.CreateDirectory(dataPath);
        }
    }

    public override Task<List<string>> ListDataFilesAsync()
    {
        var dataPath = Path.Combine(_path, ContentFolderName);
        if (!Directory.Exists(dataPath)) return Task.FromResult(new List<string>());
        var files = Directory.GetFiles(dataPath).Select(Path.GetFileName).Cast<string>().ToList();
        return Task.FromResult(files);
    }

    public override Task<byte[]> ReadDataFileAsync(string fileName)
    {
        var filePath = Path.Combine(_path, ContentFolderName, fileName);
        return File.ReadAllBytesAsync(filePath);
    }

    public override Task WriteDataFileAsync(string fileName, byte[] content)
    {
        EnsureDataDirectory();
        var filePath = Path.Combine(_path, ContentFolderName, fileName);
        return File.WriteAllBytesAsync(filePath, content);
    }

    public override Task DeleteDataFileAsync(string fileName)
    {
        var filePath = Path.Combine(_path, ContentFolderName, fileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
        return Task.CompletedTask;
    }

    public override Task<bool> HasMarkerAsync(string markerName)
    {
        var markerPath = Path.Combine(_path, markerName);
        return Task.FromResult(File.Exists(markerPath));
    }

    public override async Task WriteMarkerAsync(string markerName, string? content = null)
    {
        if (!Directory.Exists(_path)) Directory.CreateDirectory(_path);
        var markerPath = Path.Combine(_path, markerName);
        await File.WriteAllTextAsync(markerPath, content ?? "");
    }

    public override Task DeleteMarkerAsync(string markerName)
    {
        var markerPath = Path.Combine(_path, markerName);
        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }
        return Task.CompletedTask;
    }
}
