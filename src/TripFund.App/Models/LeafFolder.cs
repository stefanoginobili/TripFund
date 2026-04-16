using System.Collections.Generic;
using System.Threading.Tasks;

namespace TripFund.App.Models;

public abstract class LeafFolder
{
    public abstract Task<Dictionary<string, string>> GetMetadataAsync();
    public abstract Task SaveMetadataAsync(Dictionary<string, string> metadata);
    
    public abstract Task<bool> IsDataEmptyAsync();
    public abstract Task EnsureDataDirectoryAsync();
    
    public abstract Task<List<string>> ListDataFilesAsync();
    public abstract Task<byte[]> ReadDataFileAsync(string fileName);
    public abstract Task WriteDataFileAsync(string fileName, byte[] content);
    public abstract Task DeleteDataFileAsync(string fileName);

    public abstract Task<bool> HasMarkerAsync(string markerName);
    public abstract Task WriteMarkerAsync(string markerName, string? content = null);
    public abstract Task DeleteMarkerAsync(string markerName);

    // Helper methods for metadata
    public async Task<string?> GetMetadataValueAsync(string key)
    {
        var metadata = await GetMetadataAsync();
        return metadata.TryGetValue(key, out var value) ? value : null;
    }

    public async Task SetMetadataValueAsync(string key, string value)
    {
        var metadata = await GetMetadataAsync();
        metadata[key] = value;
        await SaveMetadataAsync(metadata);
    }
}
