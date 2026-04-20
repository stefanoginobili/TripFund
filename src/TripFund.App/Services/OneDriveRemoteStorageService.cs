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

    public async Task<TripConfig?> GetRemoteTripConfigAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider != "onedrive") return null;

        if (!parameters.TryGetValue("folderId", out var folderId)) return null;
        parameters.TryGetValue("driveId", out var driveId);

        await EnsureAuthenticatedAsync(parameters);
        
        // In Microsoft Graph, we browse config_versioned/trip_config.json
        // 1. Look for config_versioned folder
        var configFolder = await GetChildItemAsync(folderId, "config_versioned", parameters);
        if (configFolder == null || configFolder.Folder == null) return null;

        // 2. Look for latest version folder in config
        var children = await ListChildrenAsync(configFolder.Id, parameters);
        var folderNames = children.Where(v => v.Folder != null).Select(v => v.Name);
        
        string? latestVersionName;
        try
        {
            latestVersionName = _engine.GetLatestVersionFolder(folderNames);
        }
        catch (VersionedFolderConflictException)
        {
            return null;
        }
            
        if (latestVersionName == null) return null;
        var latestVersion = children.First(c => c.Name == latestVersionName);

        // 3. Find .data subfolder inside the version folder
        var dataFolder = await GetChildItemAsync(latestVersion.Id, ".data", parameters);
        if (dataFolder == null || dataFolder.Folder == null) return null;

        // 4. Find trip_config.json inside the .data folder
        var configFile = await GetChildItemAsync(dataFolder.Id, "trip_config.json", parameters);
        if (configFile == null) return null;

        var configJson = await DownloadFileContentAsync(configFile.Id, parameters);
        if (configJson == null) return null;

        return JsonSerializer.Deserialize<TripConfig>(configJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
        var url = string.IsNullOrEmpty(driveId)
            ? $"{_graphBaseUrl}/me/drive/items/{parentId}:/{name}:/content"
            : $"{_graphBaseUrl}/drives/{driveId}/items/{parentId}:/{name}:/content";

        var parentName = _idToNameCache.TryGetValue(parentId, out var n) ? $"'{n}'" : $"ID: {parentId}";
        Logger?.LogApiCall("PUT", url, $"Uploading file '{name}' to parent folder {parentName}");

        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", parameters["accessToken"]);
        request.Content = new ByteArrayContent(content);

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
}
