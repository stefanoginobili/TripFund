using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TripFund.App.Models;

namespace TripFund.App.Services;

public class OneDriveRemoteStorageService : IRemoteStorageService, IRemoteFileSystem
{
    public event Action<string, bool>? OnSyncStateChanged;
    private readonly HttpClient _httpClient;
    private readonly IWebAuthenticator _authenticator;
    private readonly LocalTripStorageService _localStorage;
    private readonly IMicrosoftAuthConfiguration _config;
    private readonly RemoteStorageSyncEngine _syncEngine;
    private readonly VersionedStorageEngine _engine = new();
    private readonly SemaphoreSlim _authSemaphore = new(1, 1);
    private string? _accessToken;
    private string? _refreshToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

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
    public async Task<string?> GetAccessTokenAsync()
    {
        await EnsureAuthenticatedAsync();
        return _accessToken;
    }

    public async Task<TripConfig?> GetRemoteTripConfigAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider != "onedrive") return null;

        if (!parameters.TryGetValue("folderId", out var folderId)) return null;
        parameters.TryGetValue("driveId", out var driveId);

        await EnsureAuthenticatedAsync(parameters);
        
        // In Microsoft Graph, we browse metadata/trip_config.json
        // 1. Look for metadata folder
        var metadataFolder = await GetChildItemAsync(folderId, "metadata", driveId);
        if (metadataFolder == null || metadataFolder.Folder == null) return null;

        // 2. Look for latest version folder in metadata
        var children = await ListChildrenAsync(metadataFolder.Id, driveId);
        var folderNames = children.Where(v => v.Folder != null).Select(v => v.Name);
        
        string? latestVersionName;
        try
        {
            latestVersionName = _engine.GetLatestVersionFolder(folderNames);
        }
        catch (VersionConflictException)
        {
            // For now, we return null or handle conflict. 
            // The requirement says "return null or throw", 
            // since this is GetRemoteTripConfig, returning null is safe as it indicates "no definitive config".
            return null;
        }
            
        if (latestVersionName == null) return null;
        var latestVersion = children.First(c => c.Name == latestVersionName);

        // 3. Find trip_config.json inside the version folder
        var configFile = await GetChildItemAsync(latestVersion.Id, "trip_config.json", driveId);
        if (configFile == null) return null;

        var configJson = await DownloadFileContentAsync(configFile.Id, driveId);
        if (configJson == null) return null;

        return JsonSerializer.Deserialize<TripConfig>(configJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public async Task<bool> IsRemoteLocationEmptyAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider != "onedrive") return false;

        if (!parameters.TryGetValue("folderId", out var folderId)) return false;
        parameters.TryGetValue("driveId", out var driveId);

        await EnsureAuthenticatedAsync(parameters);

        var children = await ListChildrenAsync(folderId, driveId);
        return children.Count == 0;
    }

    public string? GetRemoteUniqueId(string provider, Dictionary<string, string> parameters)
    {
        if (provider != "onedrive") return null;
        return parameters.TryGetValue("folderId", out var folderId) ? folderId : null;
    }

    public bool IsSyncing(string tripSlug) => false;

    public async Task SynchronizeAsync(string tripSlug)
    {
        await _syncEngine.SynchronizeAsync(tripSlug, this, (s, b) => OnSyncStateChanged?.Invoke(s, b));
    }

    async Task IRemoteFileSystem.EnsureAuthenticatedAsync(Dictionary<string, string> parameters)
    {
        await EnsureAuthenticatedAsync(parameters);
    }

    async Task<List<RemoteItem>> IRemoteFileSystem.ListChildrenAsync(string folderId, Dictionary<string, string> parameters)
    {
        var driveId = parameters.TryGetValue("driveId", out var d) ? d : null;
        var items = await ListChildrenAsync(folderId, driveId);
        return items.Select(i => new RemoteItem
        {
            Id = i.Id,
            Name = i.Name,
            IsFolder = i.Folder != null,
            ETag = i.ETag
        }).ToList();
    }

    async Task<RemoteItem?> IRemoteFileSystem.GetChildItemAsync(string parentId, string name, Dictionary<string, string> parameters)
    {
        var driveId = parameters.TryGetValue("driveId", out var d) ? d : null;
        var item = await GetChildItemAsync(parentId, name, driveId);
        if (item == null) return null;
        return new RemoteItem
        {
            Id = item.Id,
            Name = item.Name,
            IsFolder = item.Folder != null,
            ETag = item.ETag
        };
    }

    async Task<byte[]?> IRemoteFileSystem.DownloadFileContentAsync(string fileId, Dictionary<string, string> parameters)
    {
        var driveId = parameters.TryGetValue("driveId", out var d) ? d : null;
        return await DownloadFileContentAsync(fileId, driveId);
    }

    async Task<RemoteItem?> IRemoteFileSystem.CreateFolderAsync(string parentId, string name, Dictionary<string, string> parameters)
    {
        var driveId = parameters.TryGetValue("driveId", out var d) ? d : null;
        var item = await CreateFolderAsync(parentId, name, driveId);
        if (item == null) return null;
        return new RemoteItem
        {
            Id = item.Id,
            Name = item.Name,
            IsFolder = true,
            ETag = item.ETag
        };
    }

    async Task<RemoteItem?> IRemoteFileSystem.UploadFileAsync(string parentId, string name, byte[] content, Dictionary<string, string> parameters)
    {
        var driveId = parameters.TryGetValue("driveId", out var d) ? d : null;
        var item = await UploadFileAsync(parentId, name, content, driveId);
        if (item == null) return null;
        return new RemoteItem
        {
            Id = item.Id,
            Name = item.Name,
            IsFolder = false,
            ETag = item.ETag
        };
    }

    async Task IRemoteFileSystem.DeleteFileAsync(string fileId, Dictionary<string, string> parameters)
    {
        var driveId = parameters.TryGetValue("driveId", out var d) ? d : null;
        await DeleteFileAsync(fileId, driveId);
    }

    private async Task DeleteFileAsync(string fileId, string? driveId)
    {
        var url = string.IsNullOrEmpty(driveId)
            ? $"{_graphBaseUrl}/me/drive/items/{fileId}"
            : $"{_graphBaseUrl}/drives/{driveId}/items/{fileId}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception("Failed to delete file from OneDrive");
        }
    }

    #region Graph API Helpers

    private async Task<List<OneDriveItemInternal>> ListChildrenAsync(string folderId, string? driveId)
    {
        var url = string.IsNullOrEmpty(driveId) 
            ? $"{_graphBaseUrl}/me/drive/items/{folderId}/children"
            : $"{_graphBaseUrl}/drives/{driveId}/items/{folderId}/children";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return new();

        var result = await response.Content.ReadFromJsonAsync<OneDriveListResponseInternal>();
        return result?.Value ?? new();
    }

    private async Task<OneDriveItemInternal?> GetChildItemAsync(string parentId, string name, string? driveId)
    {
        var url = string.IsNullOrEmpty(driveId)
            ? $"{_graphBaseUrl}/me/drive/items/{parentId}:/{name}"
            : $"{_graphBaseUrl}/drives/{driveId}/items/{parentId}:/{name}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<OneDriveItemInternal>();
    }

    private async Task<byte[]?> DownloadFileContentAsync(string fileId, string? driveId)
    {
        var url = string.IsNullOrEmpty(driveId)
            ? $"{_graphBaseUrl}/me/drive/items/{fileId}/content"
            : $"{_graphBaseUrl}/drives/{driveId}/items/{fileId}/content";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<OneDriveItemInternal?> CreateFolderAsync(string parentId, string name, string? driveId)
    {
        var url = string.IsNullOrEmpty(driveId)
            ? $"{_graphBaseUrl}/me/drive/items/{parentId}/children"
            : $"{_graphBaseUrl}/drives/{driveId}/items/{parentId}/children";

        var body = new { name = name, folder = new { }, @microsoft_graph_conflictBehavior = "replace" };
        
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Content = JsonContent.Create(body);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<OneDriveItemInternal>();
    }

    private async Task<OneDriveItemInternal?> UploadFileAsync(string parentId, string name, byte[] content, string? driveId)
    {
        var url = string.IsNullOrEmpty(driveId)
            ? $"{_graphBaseUrl}/me/drive/items/{parentId}:/{name}:/content"
            : $"{_graphBaseUrl}/drives/{driveId}/items/{parentId}:/{name}:/content";

        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Content = new ByteArrayContent(content);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<OneDriveItemInternal>();
    }

    #endregion

    #region Authentication

    private async Task EnsureAuthenticatedAsync(Dictionary<string, string>? parameters = null)
    {
        if (_accessToken != null && DateTime.Now < _tokenExpiry) return;

        await _authSemaphore.WaitAsync();
        try
        {
            if (_accessToken != null && DateTime.Now < _tokenExpiry) return;

            if (_refreshToken == null && parameters != null && parameters.TryGetValue("refreshToken", out var storedToken))
            {
                _refreshToken = storedToken;
            }

            if (_refreshToken != null)
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

    private async Task AuthenticateAsync(Dictionary<string, string>? parameters = null)
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

    private async Task ExchangeCodeForTokenAsync(string code, string redirectUri, Dictionary<string, string>? parameters = null)
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
            _accessToken = tokenResponse.AccessToken;
            _refreshToken = tokenResponse.RefreshToken;
            _tokenExpiry = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn - 30);

            if (parameters != null)
            {
                parameters["refreshToken"] = _refreshToken;
            }
        }
    }

    private async Task RefreshTokenAsync(Dictionary<string, string>? parameters = null)
    {
        var tokenUrl = $"https://login.microsoftonline.com/{_config.MicrosoftTenantId}/oauth2/v2.0/token";
        var body = new Dictionary<string, string>
        {
            ["client_id"] = _config.MicrosoftClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = _refreshToken!,
            ["scope"] = "Files.ReadWrite offline_access"
        };

        var response = await _httpClient.PostAsync(tokenUrl, new FormUrlEncodedContent(body));
        if (response.IsSuccessStatusCode)
        {
            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();
            if (tokenResponse != null)
            {
                _accessToken = tokenResponse.AccessToken;
                _refreshToken = tokenResponse.RefreshToken;
                _tokenExpiry = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn - 30);

                if (parameters != null)
                {
                    parameters["refreshToken"] = _refreshToken;
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
        [JsonPropertyName("@microsoft.graph.downloadUrl")] public string? DownloadUrl { get; set; }
        [JsonPropertyName("eTag")] public string? ETag { get; set; }
    }
}
