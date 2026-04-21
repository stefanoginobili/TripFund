using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TripFund.App.Models;

namespace TripFund.App.Services;

public class OneDriveRemoteStorageService : IRemoteStorageService, IRemoteFileSystem
{
    public event Action<string, bool>? OnSyncStateChanged;
    
    private readonly AsyncLocal<IRemoteStorageLogger?> _logger = new();
    public IRemoteStorageLogger? Logger 
    { 
        get => _logger.Value;
        set => _logger.Value = value;
    }

    private readonly HttpClient _httpClient;
    private readonly IWebAuthenticator _authenticator;
    private readonly LocalTripStorageService _localStorage;
    private readonly IMicrosoftAuthConfiguration _config;
    private readonly RemoteStorageSyncEngine _syncEngine;
    private readonly VersionedStorageEngine _engine = new();
    private readonly SemaphoreSlim _authSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<string, string> _idToNameCache = new();

    private readonly string _graphBaseUrl;

    public OneDriveRemoteStorageService(
        IHttpClientFactory httpClientFactory,
        IWebAuthenticator authenticator,
        LocalTripStorageService localStorage,
        IMicrosoftAuthConfiguration config,
        RemoteStorageSyncEngine syncEngine,
        string graphBaseUrl = "https://graph.microsoft.com/v1.0")
    {
        _httpClient = httpClientFactory.CreateClient(nameof(OneDriveRemoteStorageService));
        _authenticator = authenticator;
        _localStorage = localStorage;
        _config = config;
        _syncEngine = syncEngine;
        _graphBaseUrl = graphBaseUrl.TrimEnd('/');
    }

    public async Task<Dictionary<string, string>?> AuthenticateUserAsync()
    {
        var parameters = new Dictionary<string, string>();
        await AuthenticateAsync(parameters);
        if (parameters.ContainsKey("accessToken"))
        {
            return parameters;
        }
        return null;
    }

    public async Task<RemoteTripMetadata?> GetRemoteTripMetadataAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider != "onedrive") return null;

        if (!parameters.TryGetValue("folderId", out var folderId)) return null;

        await EnsureAuthenticatedAsync(parameters);
        
        var tripFile = await GetChildItemAsync(folderId, ".tripfund", parameters);
        if (tripFile == null) return null;

        var content = await DownloadFileContentAsync(tripFile.Id, parameters);
        if (content == null) return null;

        var text = System.Text.Encoding.UTF8.GetString(content);
        var metadata = new RemoteTripMetadata();
        
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('=', 2);
            if (parts.Length < 2) continue;

            var key = parts[0].Trim();
            var val = parts[1].Trim();

            if (key == "contentType") metadata.IsValid = (val == "tripfund/trip");
            else if (key == "tripSlug") metadata.TripSlug = val;
            else if (key == "author") metadata.Author = val;
            else if (key == "createdAt") 
            {
                if (DateTime.TryParse(val, out var dt)) metadata.CreatedAt = dt;
            }
        }

        return metadata.IsValid ? metadata : null;
    }

    public async Task InitializeRemoteLocationAsync(string tripSlug, string provider, Dictionary<string, string> parameters)
    {
        if (provider != "onedrive") return;

        if (!parameters.TryGetValue("folderId", out var folderId)) return;

        await EnsureAuthenticatedAsync(parameters);

        var settings = await _localStorage.GetAppSettingsAsync();
        var author = settings?.AuthorName ?? "Unknown";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("contentType=tripfund/trip");
        sb.AppendLine($"tripSlug={tripSlug}");
        sb.AppendLine($"author={author}");
        sb.AppendLine($"createdAt={DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}");

        var content = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        await UploadFileAsync(folderId, ".tripfund", content, parameters);
    }

    public async Task<bool> IsRemoteLocationEmptyAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider != "onedrive") return false;

        if (!parameters.TryGetValue("folderId", out var folderId)) return false;

        await EnsureAuthenticatedAsync(parameters);

        var children = await ListChildrenAsync(folderId, parameters);
        return children.Count == 0;
    }

    public string? GetRemoteUniqueId(string provider, Dictionary<string, string> parameters)
    {
        if (provider != "onedrive") return null;
        return parameters.TryGetValue("folderId", out var folderId) ? folderId : null;
    }

    public bool IsSyncing(string tripSlug) => false;

    public virtual async Task SynchronizeAsync(string tripSlug)
    {
        await _syncEngine.SynchronizeAsync(tripSlug, this, (s, b) => OnSyncStateChanged?.Invoke(s, b));
    }

    async Task IRemoteFileSystem.EnsureAuthenticatedAsync(Dictionary<string, string> parameters)
    {
        await EnsureAuthenticatedAsync(parameters);
    }

    async Task<List<RemoteItem>> IRemoteFileSystem.ListChildrenAsync(string folderId, Dictionary<string, string> parameters)
    {
        var items = await ListChildrenAsync(folderId, parameters);
        return items.Select(i => new RemoteItem
        {
            Id = i.Id,
            Name = i.Name,
            IsFolder = i.Folder != null || i.RemoteItem?.Folder != null,
            IsRemote = i.RemoteItem != null
        }).ToList();
    }

    async Task<RemoteItem?> IRemoteFileSystem.GetChildItemAsync(string parentId, string name, Dictionary<string, string> parameters)
    {
        var item = await GetChildItemAsync(parentId, name, parameters);
        if (item == null) return null;
        return new RemoteItem
        {
            Id = item.Id,
            Name = item.Name,
            IsFolder = item.Folder != null || item.RemoteItem?.Folder != null,
            IsRemote = item.RemoteItem != null
        };
    }

    async Task<byte[]?> IRemoteFileSystem.DownloadFileContentAsync(string fileId, Dictionary<string, string> parameters)
    {
        return await DownloadFileContentAsync(fileId, parameters);
    }

    async Task<RemoteItem?> IRemoteFileSystem.CreateFolderAsync(string parentId, string name, Dictionary<string, string> parameters)
    {
        var item = await CreateFolderAsync(parentId, name, parameters);
        if (item == null) return null;
        return new RemoteItem
        {
            Id = item.Id,
            Name = item.Name,
            IsFolder = true
        };
    }

    async Task<RemoteItem?> IRemoteFileSystem.UploadFileAsync(string parentId, string name, byte[] content, Dictionary<string, string> parameters)
    {
        var item = await UploadFileAsync(parentId, name, content, parameters);
        if (item == null) return null;
        return new RemoteItem
        {
            Id = item.Id,
            Name = item.Name,
            IsFolder = false
        };
    }

    async Task IRemoteFileSystem.DeleteFileAsync(string fileId, Dictionary<string, string> parameters)
    {
        await DeleteFileAsync(fileId, parameters);
    }

    async Task IRemoteFileSystem.RenameAsync(string itemId, string newName, Dictionary<string, string> parameters)
    {
        await RenameAsync(itemId, newName, parameters);
    }

    private async Task DeleteFileAsync(string fileId, Dictionary<string, string> parameters)
    {
        var driveId = parameters.TryGetValue("driveId", out var d) ? d : null;
        var url = string.IsNullOrEmpty(driveId)
            ? $"{_graphBaseUrl}/me/drive/items/{fileId}"
            : $"{_graphBaseUrl}/drives/{driveId}/items/{fileId}";

        var itemName = _idToNameCache.TryGetValue(fileId, out var n) ? $"'{n}'" : $"ID: {fileId}";
        Logger?.LogApiCall("DELETE", url, $"Deleting file/folder {itemName}");

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", parameters["accessToken"]);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Logger?.LogError($"Failed to delete file {fileId}: {response.StatusCode} - {error}");
            throw new Exception("Failed to delete file from OneDrive");
        }
    }

    private async Task RenameAsync(string itemId, string newName, Dictionary<string, string> parameters)
    {
        var driveId = parameters.TryGetValue("driveId", out var d) ? d : null;
        var url = string.IsNullOrEmpty(driveId)
            ? $"{_graphBaseUrl}/me/drive/items/{itemId}"
            : $"{_graphBaseUrl}/drives/{driveId}/items/{itemId}";

        var oldName = _idToNameCache.TryGetValue(itemId, out var n) ? $"'{n}'" : $"ID: {itemId}";
        Logger?.LogApiCall("PATCH", url, $"Renaming {oldName} to '{newName}'");

        var body = new { name = newName };

        using var request = new HttpRequestMessage(HttpMethod.Patch, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", parameters["accessToken"]);
        request.Content = JsonContent.Create(body);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Logger?.LogError($"Failed to rename item {itemId} to {newName}: {response.StatusCode} - {error}");
            throw new Exception($"Failed to rename item on OneDrive: {error}");
        }

        var item = await response.Content.ReadFromJsonAsync<OneDriveItemInternal>();
        if (item != null && !string.IsNullOrEmpty(item.Id) && !string.IsNullOrEmpty(item.Name))
        {
            _idToNameCache[item.Id] = item.Name;
        }
    }

    #region Graph API Helpers

    private async Task<List<OneDriveItemInternal>> ListChildrenAsync(string folderId, Dictionary<string, string> parameters)
    {
        var driveId = parameters.TryGetValue("driveId", out var d) ? d : null;
        var url = string.IsNullOrEmpty(driveId) 
            ? $"{_graphBaseUrl}/me/drive/items/{folderId}/children"
            : $"{_graphBaseUrl}/drives/{driveId}/items/{folderId}/children";

        var folderName = _idToNameCache.TryGetValue(folderId, out var n) ? $"'{n}'" : $"ID: {folderId}";
        Logger?.LogApiCall("GET", url, $"Listing children for folder {folderName}");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", parameters["accessToken"]);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Logger?.LogError($"Failed to list children for {folderId}: {response.StatusCode} - {error}");
            return new();
        }

        var result = await response.Content.ReadFromJsonAsync<OneDriveListResponseInternal>();
        if (result?.Value != null)
        {
            foreach (var item in result.Value)
            {
                if (!string.IsNullOrEmpty(item.Id) && !string.IsNullOrEmpty(item.Name))
                {
                    _idToNameCache[item.Id] = item.Name;
                }
            }
        }
        return result?.Value ?? new();
    }

    private async Task<OneDriveItemInternal?> GetChildItemAsync(string parentId, string name, Dictionary<string, string> parameters)
    {
        var driveId = parameters.TryGetValue("driveId", out var d) ? d : null;
        var url = string.IsNullOrEmpty(driveId)
            ? $"{_graphBaseUrl}/me/drive/items/{parentId}:/{name}"
            : $"{_graphBaseUrl}/drives/{driveId}/items/{parentId}:/{name}";

        var parentName = _idToNameCache.TryGetValue(parentId, out var n) ? $"'{n}'" : $"ID: {parentId}";
        Logger?.LogApiCall("GET", url, $"Getting item '{name}' in parent folder {parentName}");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", parameters["accessToken"]);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                var error = await response.Content.ReadAsStringAsync();
                Logger?.LogError($"Failed to get item '{name}' in {parentId}: {response.StatusCode} - {error}");
            }
            return null;
        }

        var item = await response.Content.ReadFromJsonAsync<OneDriveItemInternal>();
        if (item != null && !string.IsNullOrEmpty(item.Id) && !string.IsNullOrEmpty(item.Name))
        {
            _idToNameCache[item.Id] = item.Name;
        }
        return item;
    }

    private async Task<byte[]?> DownloadFileContentAsync(string fileId, Dictionary<string, string> parameters)
    {
        var driveId = parameters.TryGetValue("driveId", out var d) ? d : null;
        var url = string.IsNullOrEmpty(driveId)
            ? $"{_graphBaseUrl}/me/drive/items/{fileId}/content"
            : $"{_graphBaseUrl}/drives/{driveId}/items/{fileId}/content";

        var fileName = _idToNameCache.TryGetValue(fileId, out var n) ? $"'{n}'" : $"ID: {fileId}";
        Logger?.LogApiCall("GET", url, $"Downloading content for file {fileName}");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", parameters["accessToken"]);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Logger?.LogError($"Failed to download content for {fileId}: {response.StatusCode} - {error}");
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<OneDriveItemInternal?> CreateFolderAsync(string parentId, string name, Dictionary<string, string> parameters)
    {
        var driveId = parameters.TryGetValue("driveId", out var d) ? d : null;
        var url = string.IsNullOrEmpty(driveId)
            ? $"{_graphBaseUrl}/me/drive/items/{parentId}/children"
            : $"{_graphBaseUrl}/drives/{driveId}/items/{parentId}/children";

        var parentName = _idToNameCache.TryGetValue(parentId, out var n) ? $"'{n}'" : $"ID: {parentId}";
        Logger?.LogApiCall("POST", url, $"Creating folder '{name}' in parent folder {parentName}");

        var body = new { name = name, folder = new { }, @microsoft_graph_conflictBehavior = "replace" };
        
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", parameters["accessToken"]);
        request.Content = JsonContent.Create(body);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Logger?.LogError($"Failed to create folder '{name}' in {parentId}: {response.StatusCode} - {error}");
            return null;
        }

        var item = await response.Content.ReadFromJsonAsync<OneDriveItemInternal>();
        if (item != null && !string.IsNullOrEmpty(item.Id) && !string.IsNullOrEmpty(item.Name))
        {
            _idToNameCache[item.Id] = item.Name;
        }
        return item;
    }

    private async Task<OneDriveItemInternal?> UploadFileAsync(string parentId, string name, byte[] content, Dictionary<string, string> parameters)
    {
        var driveId = parameters.TryGetValue("driveId", out var d) ? d : null;
        var parentName = _idToNameCache.TryGetValue(parentId, out var n) ? $"'{n}'" : $"ID: {parentId}";
        
        // Threshold: 2 MB
        const int uploadSessionThreshold = 2 * 1024 * 1024; 

        if (content.Length <= uploadSessionThreshold)
        {
            // --- SIMPLE UPLOAD ---
            var url = string.IsNullOrEmpty(driveId)
                ? $"{_graphBaseUrl}/me/drive/items/{parentId}:/{name}:/content"
                : $"{_graphBaseUrl}/drives/{driveId}/items/{parentId}:/{name}:/content";

            Logger?.LogApiCall("PUT", url, $"Uploading file '{name}' to parent folder {parentName} (Simple Upload)");

            using var request = new HttpRequestMessage(HttpMethod.Put, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", parameters["accessToken"]);
            
            var byteArrayContent = new ByteArrayContent(content);
            var contentType = name.EndsWith(".zip") || name.EndsWith(".zip.part") 
                ? "application/zip" 
                : (name.EndsWith(".json") ? "application/json" : "application/octet-stream");
            byteArrayContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            request.Content = byteArrayContent;

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Logger?.LogError($"Failed to upload file '{name}' to {parentId}: {response.StatusCode} - {error}");
                return null;
            }

            var item = await response.Content.ReadFromJsonAsync<OneDriveItemInternal>();
            if (item != null && !string.IsNullOrEmpty(item.Id) && !string.IsNullOrEmpty(item.Name))
            {
                _idToNameCache[item.Id] = item.Name;
            }
            return item;
        }
        else
        {
            // --- UPLOAD SESSION (Resumable) ---
            var createSessionUrl = string.IsNullOrEmpty(driveId)
                ? $"{_graphBaseUrl}/me/drive/items/{parentId}:/{name}:/createUploadSession"
                : $"{_graphBaseUrl}/drives/{driveId}/items/{parentId}:/{name}:/createUploadSession";

            Logger?.LogApiCall("POST", createSessionUrl, $"Creating upload session for file '{name}' in parent folder {parentName}");

            var sessionBody = new { item = new { @microsoft_graph_conflictBehavior = "replace" } };
            using var sessionReq = new HttpRequestMessage(HttpMethod.Post, createSessionUrl);
            sessionReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", parameters["accessToken"]);
            sessionReq.Content = JsonContent.Create(sessionBody);

            var sessionRes = await _httpClient.SendAsync(sessionReq);
            if (!sessionRes.IsSuccessStatusCode)
            {
                var error = await sessionRes.Content.ReadAsStringAsync();
                Logger?.LogError($"Failed to create upload session for '{name}' in {parentId}: {sessionRes.StatusCode} - {error}");
                return null;
            }

            var sessionInfo = await sessionRes.Content.ReadFromJsonAsync<UploadSessionResponseInternal>();
            if (sessionInfo == null || string.IsNullOrEmpty(sessionInfo.UploadUrl))
            {
                Logger?.LogError($"Upload session response was invalid for '{name}'");
                return null;
            }

            // Microsoft requires chunks to be a multiple of 320 KiB. We'll use 3.2 MB chunks.
            const int chunkSize = 320 * 1024 * 10; 
            OneDriveItemInternal? finalItem = null;

            for (int offset = 0; offset < content.Length; offset += chunkSize)
            {
                int currentChunkSize = Math.Min(chunkSize, content.Length - offset);
                var chunk = new byte[currentChunkSize];
                Array.Copy(content, offset, chunk, 0, currentChunkSize);

                using var chunkReq = new HttpRequestMessage(HttpMethod.Put, sessionInfo.UploadUrl);
                var chunkContent = new ByteArrayContent(chunk);
                chunkContent.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + currentChunkSize - 1, content.Length);
                chunkReq.Content = chunkContent;

                Logger?.LogApiCall("PUT", sessionInfo.UploadUrl, $"Uploading chunk {offset}-{offset + currentChunkSize - 1} of {content.Length}");

                var chunkRes = await _httpClient.SendAsync(chunkReq);
                if (!chunkRes.IsSuccessStatusCode)
                {
                    var error = await chunkRes.Content.ReadAsStringAsync();
                    Logger?.LogError($"Failed to upload chunk for '{name}': {chunkRes.StatusCode} - {error}");
                    return null;
                }

                // HTTP 201 Created or 200 OK means the upload session is complete
                if (chunkRes.StatusCode == System.Net.HttpStatusCode.Created || chunkRes.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    finalItem = await chunkRes.Content.ReadFromJsonAsync<OneDriveItemInternal>();
                }
            }

            if (finalItem != null && !string.IsNullOrEmpty(finalItem.Id) && !string.IsNullOrEmpty(finalItem.Name))
            {
                _idToNameCache[finalItem.Id] = finalItem.Name;
            }

            return finalItem;
        }
    }

    #endregion

    #region Authentication

    private async Task EnsureAuthenticatedAsync(Dictionary<string, string> parameters)
    {
        if (parameters.TryGetValue("accessToken", out var accessToken) && 
            parameters.TryGetValue("accessTokenExpiry", out var expiryStr) && 
            DateTime.TryParse(expiryStr, out var expiry) && 
            DateTime.Now < expiry)
        {
            return;
        }

        await _authSemaphore.WaitAsync();
        try
        {
            if (parameters.TryGetValue("accessToken", out accessToken) && 
                parameters.TryGetValue("accessTokenExpiry", out expiryStr) && 
                DateTime.TryParse(expiryStr, out expiry) && 
                DateTime.Now < expiry)
            {
                return;
            }

            if (parameters.TryGetValue("refreshToken", out var refreshToken) && !string.IsNullOrEmpty(refreshToken))
            {
                await RefreshTokenAsync(parameters);
                return;
            }

            await AuthenticateAsync(parameters);
        }
        finally
        {
            _authSemaphore.Release();
        }
    }

    private async Task AuthenticateAsync(Dictionary<string, string> parameters)
    {
        var scope = "Files.ReadWrite offline_access";
        var redirectUri = $"msal{_config.MicrosoftClientId}://auth";
        var authUrl = $"https://login.microsoftonline.com/{_config.MicrosoftTenantId}/oauth2/v2.0/authorize?client_id={_config.MicrosoftClientId}&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString(scope)}";

        var authResult = await _authenticator.AuthenticateAsync(new WebAuthenticatorOptions
        {
            Url = new Uri(authUrl),
            CallbackUrl = new Uri(redirectUri)
        });

        if (authResult?.Properties.TryGetValue("code", out var code) == true)
        {
            await ExchangeCodeForTokenAsync(code, redirectUri, parameters);
        }
    }

    private async Task ExchangeCodeForTokenAsync(string code, string redirectUri, Dictionary<string, string> parameters)
    {
        var tokenUrl = $"https://login.microsoftonline.com/{_config.MicrosoftTenantId}/oauth2/v2.0/token";
        var body = new Dictionary<string, string>
        {
            ["client_id"] = _config.MicrosoftClientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["scope"] = "Files.ReadWrite offline_access"
        };

        var response = await _httpClient.PostAsync(tokenUrl, new FormUrlEncodedContent(body));
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (tokenResponse != null)
        {
            parameters["accessToken"] = tokenResponse.AccessToken;
            parameters["accessTokenExpiry"] = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn - 30).ToString("O");
            if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
            {
                parameters["refreshToken"] = tokenResponse.RefreshToken;
            }
        }
    }

    private async Task RefreshTokenAsync(Dictionary<string, string> parameters)
    {
        var tokenUrl = $"https://login.microsoftonline.com/{_config.MicrosoftTenantId}/oauth2/v2.0/token";
        var body = new Dictionary<string, string>
        {
            ["client_id"] = _config.MicrosoftClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = parameters["refreshToken"],
            ["scope"] = "Files.ReadWrite offline_access"
        };

        var response = await _httpClient.PostAsync(tokenUrl, new FormUrlEncodedContent(body));
        if (response.IsSuccessStatusCode)
        {
            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();
            if (tokenResponse != null)
            {
                parameters["accessToken"] = tokenResponse.AccessToken;
                parameters["accessTokenExpiry"] = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn - 30).ToString("O");
                if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
                {
                    parameters["refreshToken"] = tokenResponse.RefreshToken;
                }
                return;
            }
        }

        // If refresh fails, re-authenticate
        await AuthenticateAsync(parameters);
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = string.Empty;
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    #endregion

    private class OneDriveListResponseInternal
    {
        [JsonPropertyName("value")] public List<OneDriveItemInternal> Value { get; set; } = new();
    }

    private class OneDriveItemInternal
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("folder")] public object? Folder { get; set; }
        [JsonPropertyName("remoteItem")] public RemoteItemInternal? RemoteItem { get; set; }
        [JsonPropertyName("@microsoft.graph.downloadUrl")] public string? DownloadUrl { get; set; }
    }

    private class RemoteItemInternal
    {
        [JsonPropertyName("folder")] public object? Folder { get; set; }
    }

    private class UploadSessionResponseInternal
    {
        [JsonPropertyName("uploadUrl")] public string UploadUrl { get; set; } = string.Empty;
        [JsonPropertyName("expirationDateTime")] public DateTime? ExpirationDateTime { get; set; }
    }
}
