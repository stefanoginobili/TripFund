using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TripFund.App.Models;

namespace TripFund.App.Services;

public class OneDrivePickerService : IOneDrivePickerService
{
    private readonly HttpClient _httpClient;

    public OneDrivePickerService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<(string? FolderId, string? FolderName)> PickFolderAsync(string accessToken, string title)
    {
        // This will be invoked by the OneDrivePickerModal component or by the RemoteStorageSelector
        // In this architecture, we will handle the browsing in the Blazor UI.
        // This service will provide the backend data fetching for the UI.
        return Task.FromResult<(string?, string?)>((null, null));
    }

    public async Task<OneDriveItem> CreateFolderAsync(string accessToken, string folderName, string? parentFolderId = null, string? driveId = null)
    {
        string url;
        if (string.IsNullOrEmpty(parentFolderId))
        {
            url = "https://graph.microsoft.com/v1.0/me/drive/root/children";
        }
        else if (!string.IsNullOrEmpty(driveId))
        {
            url = $"https://graph.microsoft.com/v1.0/drives/{driveId}/items/{parentFolderId}/children";
        }
        else
        {
            url = $"https://graph.microsoft.com/v1.0/me/drive/items/{parentFolderId}/children";
        }

        var body = new { name = folderName, folder = new { }, @microsoft_graph_conflictBehavior = "rename" };
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(body);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<OneDriveItem>() ?? throw new Exception("Failed to create folder");
    }

    public async Task<List<OneDriveItem>> ListFoldersAsync(string accessToken, string? parentFolderId = null, string? driveId = null)
    {
        string url;
        if (string.IsNullOrEmpty(parentFolderId))
        {
            url = "https://graph.microsoft.com/v1.0/me/drive/root/children?$filter=folder ne null";
        }
        else if (!string.IsNullOrEmpty(driveId))
        {
            url = $"https://graph.microsoft.com/v1.0/drives/{driveId}/items/{parentFolderId}/children?$filter=folder ne null";
        }
        else
        {
            url = $"https://graph.microsoft.com/v1.0/me/drive/items/{parentFolderId}/children?$filter=folder ne null";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OneDriveListResponse>();
        return result?.Value ?? new List<OneDriveItem>();
    }

    public async Task<List<OneDriveItem>> ListSharedFoldersAsync(string accessToken)
    {
        // GET /me/drive/sharedWithMe returns DriveItems that are shared with the user.
        // We filter for items that represent a folder.
        var url = "https://graph.microsoft.com/v1.0/me/drive/sharedWithMe";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OneDriveListResponse>();
        
        // Map remote items if necessary and filter for folders
        var items = new List<OneDriveItem>();
        if (result?.Value != null)
        {
            foreach (var item in result.Value)
            {
                // In sharedWithMe, the actual item info is often inside remoteItem
                if (item.RemoteItem != null && item.RemoteItem.Folder != null)
                {
                    items.Add(new OneDriveItem
                    {
                        Id = item.RemoteItem.Id,
                        Name = item.RemoteItem.Name,
                        Folder = item.RemoteItem.Folder,
                        ParentReference = item.RemoteItem.ParentReference
                    });
                }
                else if (item.Folder != null)
                {
                    items.Add(item);
                }
            }
        }
        return items;
    }
}

public class OneDriveListResponse
{
    [JsonPropertyName("value")]
    public List<OneDriveItem> Value { get; set; } = new();
}

public class OneDriveItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("folder")]
    public object? Folder { get; set; }

    [JsonPropertyName("remoteItem")]
    public OneDriveRemoteItem? RemoteItem { get; set; }

    [JsonPropertyName("parentReference")]
    public OneDriveParentReference? ParentReference { get; set; }
}

public class OneDriveRemoteItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("folder")]
    public object? Folder { get; set; }

    [JsonPropertyName("parentReference")]
    public OneDriveParentReference? ParentReference { get; set; }
}

public class OneDriveParentReference
{
    [JsonPropertyName("driveId")]
    public string? DriveId { get; set; }
}
