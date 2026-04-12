using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TripFund.App.Models;

namespace TripFund.App.Services;

public class OneDriveRemoteStorageService : IRemoteStorageService
{
    public event Action<string, bool>? OnSyncStateChanged;
    private readonly HttpClient _httpClient;
    private readonly IWebAuthenticator _authenticator;
    private readonly LocalTripStorageService _localStorage;
    private readonly IMicrosoftAuthConfiguration _config;
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
        string graphBaseUrl = "https://graph.microsoft.com/v1.0")
    {
        _httpClient = httpClientFactory.CreateClient(nameof(OneDriveRemoteStorageService));
        _authenticator = authenticator;
        _localStorage = localStorage;
        _config = config;
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
        OnSyncStateChanged?.Invoke(tripSlug, true);
        try
        {
            var registry = await _localStorage.GetTripRegistryAsync();
            if (!registry.Trips.TryGetValue(tripSlug, out var entry) || entry.RemoteStorage == null) return;

            if (!entry.RemoteStorage.Parameters.TryGetValue("folderId", out var folderId)) return;
            entry.RemoteStorage.Parameters.TryGetValue("driveId", out var driveId);

            var oldRefreshToken = _refreshToken;
            await EnsureAuthenticatedAsync(entry.RemoteStorage.Parameters);

            if (_refreshToken != oldRefreshToken)
            {
                await _localStorage.SaveTripRegistryAsync(registry);
            }

            // Check if root is readonly by trying to write/delete a test file
            var canWrite = await CheckFolderWritePermissionAsync(folderId, driveId);
            if (entry.RemoteStorage.Readonly != !canWrite)
            {
                entry.RemoteStorage.Readonly = !canWrite;
                await _localStorage.SaveTripRegistryAsync(registry);
            }

            // 1. Download Phase
            var localTripPath = Path.Combine(_localStorage.TripsPath, tripSlug);
            await SyncDownAsync(folderId, localTripPath, driveId);

            // 2. Integrity & Conflict Check (handled by LocalTripStorageService and VersionedStorageEngine normally)
            // Here we just mark if there's a conflict
            bool hasConflict = false;
            if (Directory.Exists(Path.Combine(localTripPath, "metadata")))
            {
                if (_engine.IsInConflict(Path.Combine(localTripPath, "metadata"))) hasConflict = true;
            }

            var transDir = Path.Combine(localTripPath, "transactions");
            if (Directory.Exists(transDir))
            {
                foreach (var t in Directory.GetDirectories(transDir))
                {
                    if (_engine.IsInConflict(t))
                    {
                        hasConflict = true;
                        break;
                    }
                }
            }

            entry.RemoteStorage.HasConflicts = hasConflict;
            await _localStorage.SaveTripRegistryAsync(registry);

            if (hasConflict) return;

            // 3. Upload Phase
            if (!entry.RemoteStorage.Readonly)
            {
                await SyncUpAsync(localTripPath, folderId, driveId);
            }
        }
        finally
        {
            OnSyncStateChanged?.Invoke(tripSlug, false);
        }
    }

    private async Task<bool> CheckFolderWritePermissionAsync(string folderId, string? driveId)
    {
        try
        {
            var settings = await _localStorage.GetAppSettingsAsync();
            var deviceId = settings?.DeviceId ?? "unknown-device";
            var fileName = $".rw-test-{deviceId}";

            // Check if test file already exists
            var children = await ListChildrenAsync(folderId, driveId);
            var existing = children.FirstOrDefault(c => c.Name == fileName);
            if (existing != null)
            {
                // Try to delete existing file to confirm write permission
                try
                {
                    await DeleteFileAsync(existing.Id, driveId);
                    return true;
                }
                catch
                {
                    // If deletion of existing fails, proceed to try creation
                }
            }

            // Try to create/upload a new test file
            var uploaded = await UploadFileAsync(folderId, fileName, new byte[] { 0x01 }, driveId);
            if (uploaded == null) return false;

            // Try to delete it
            await DeleteFileAsync(uploaded.Id, driveId);
            return true;
        }
        catch
        {
            return false;
        }
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

    private async Task SyncDownAsync(string remoteFolderId, string localPath, string? driveId)
    {
        if (File.Exists(Path.Combine(localPath, ".synched"))) return;

        var children = await ListChildrenAsync(remoteFolderId, driveId);
        if (children.Count == 0) return;

        bool hasFolders = children.Any(c => c.Folder != null);
        bool hasFiles = children.Any(c => c.Folder == null && c.Name != ".synching");

        if (hasFolders && hasFiles)
        {
            throw new InvalidOperationException("Architecture constraint violation: Folder contains both files and subfolders.");
        }

        if (hasFolders)
        {
            // Node folder: Recurse into subfolders
            foreach (var child in children.Where(c => c.Folder != null))
            {
                var localChildPath = Path.Combine(localPath, child.Name);
                if (!Directory.Exists(localChildPath)) Directory.CreateDirectory(localChildPath);
                await SyncDownAsync(child.Id, localChildPath, driveId);
            }
        }
        else if (hasFiles)
        {
            // Leaf folder: Apply the "Fully Copied" rule
            bool isFullyCopiedLocally = false;
            if (Directory.Exists(localPath))
            {
                var localEntries = Directory.GetFileSystemEntries(localPath)
                    .Where(e => !e.EndsWith(".remote-etag") && Path.GetFileName(e) != ".synching" && Path.GetFileName(e) != ".synched")
                    .ToList();
                bool hasSynching = File.Exists(Path.Combine(localPath, ".synching"));
                if (localEntries.Count > 0 && !hasSynching)
                {
                    isFullyCopiedLocally = true;
                }
            }

            if (!isFullyCopiedLocally)
            {
                // Restart copy: Clear existing contents and mark with .synching
                if (Directory.Exists(localPath))
                {
                    foreach (var file in Directory.GetFiles(localPath)) File.Delete(file);
                }
                else
                {
                    Directory.CreateDirectory(localPath);
                }

                var synchingFile = Path.Combine(localPath, ".synching");
                await File.WriteAllTextAsync(synchingFile, "");

                foreach (var child in children.Where(c => c.Folder == null && c.Name != ".synching"))
                {
                    var localChildFile = Path.Combine(localPath, child.Name);
                    var content = await DownloadFileContentAsync(child.Id, driveId);
                    if (content != null)
                    {
                        await File.WriteAllBytesAsync(localChildFile, content);
                        await File.WriteAllTextAsync(localChildFile + ".remote-etag", child.ETag);
                    }
                }

                if (File.Exists(synchingFile)) File.Delete(synchingFile);
            }
            else
            {
                // Already fully copied: Check for updates to individual files
                foreach (var child in children.Where(c => c.Folder == null && c.Name != ".synching"))
                {
                    var localChildFile = Path.Combine(localPath, child.Name);
                    var metadataFile = localChildFile + ".remote-etag";
                    var remoteEtag = child.ETag;
                    var localEtag = File.Exists(metadataFile) ? await File.ReadAllTextAsync(metadataFile) : null;

                    if (remoteEtag != localEtag)
                    {
                        var content = await DownloadFileContentAsync(child.Id, driveId);
                        if (content != null)
                        {
                            await File.WriteAllBytesAsync(localChildFile, content);
                            await File.WriteAllTextAsync(metadataFile, remoteEtag);
                        }
                    }
                }
            }

            // Mark as fully synched locally
            await File.WriteAllTextAsync(Path.Combine(localPath, ".synched"), "");
        }
    }

    private async Task SyncUpAsync(string localPath, string remoteFolderId, string? driveId)
    {
        if (File.Exists(Path.Combine(localPath, ".synched"))) return;

        var localEntries = Directory.GetFileSystemEntries(localPath)
            .Where(e => !e.EndsWith(".remote-etag") && Path.GetFileName(e) != ".synching" && Path.GetFileName(e) != ".synched")
            .ToList();
        if (localEntries.Count == 0) return;

        bool hasFolders = localEntries.Any(e => Directory.Exists(e));
        bool hasFiles = localEntries.Any(e => File.Exists(e));

        if (hasFolders && hasFiles)
        {
            throw new InvalidOperationException("Architecture constraint violation: Folder contains both files and subfolders.");
        }

        var remoteChildren = await ListChildrenAsync(remoteFolderId, driveId);

        if (hasFolders)
        {
            // Node folder: Recurse into subfolders
            foreach (var entry in localEntries.Where(e => Directory.Exists(e)))
            {
                var name = Path.GetFileName(entry);
                var remoteMatch = remoteChildren.FirstOrDefault(c => c.Name == name && c.Folder != null);
                string folderId;
                if (remoteMatch != null)
                {
                    folderId = remoteMatch.Id;
                }
                else
                {
                    var newFolder = await CreateFolderAsync(remoteFolderId, name, driveId);
                    if (newFolder == null) continue;
                    folderId = newFolder.Id;
                }
                await SyncUpAsync(entry, folderId, driveId);
            }
        }
        else if (hasFiles)
        {
            // Leaf folder: Apply the "Fully Copied" rule on remote
            bool isFullyCopiedRemotely = false;
            bool hasSynching = remoteChildren.Any(c => c.Name == ".synching");
            bool isEmpty = !remoteChildren.Any(c => c.Name != ".synching");

            if (!isEmpty && !hasSynching)
            {
                isFullyCopiedRemotely = true;
            }

            if (!isFullyCopiedRemotely)
            {
                // Restart copy: Clear remote contents and mark with .synching
                foreach (var child in remoteChildren)
                {
                    await DeleteFileAsync(child.Id, driveId);
                }

                await UploadFileAsync(remoteFolderId, ".synching", new byte[] { 0x01 }, driveId);

                foreach (var entry in localEntries.Where(e => File.Exists(e)))
                {
                    var name = Path.GetFileName(entry);
                    var content = await File.ReadAllBytesAsync(entry);
                    var uploaded = await UploadFileAsync(remoteFolderId, name, content, driveId);
                    if (uploaded != null)
                    {
                        await File.WriteAllTextAsync(entry + ".remote-etag", uploaded.ETag);
                    }
                }

                // Mark as fully copied: Delete the .synching file
                var finalChildren = await ListChildrenAsync(remoteFolderId, driveId);
                var sFile = finalChildren.FirstOrDefault(c => c.Name == ".synching");
                if (sFile != null) await DeleteFileAsync(sFile.Id, driveId);
            }
            else
            {
                // Already fully copied: Check for updates or missing files
                foreach (var entry in localEntries.Where(e => File.Exists(e)))
                {
                    var name = Path.GetFileName(entry);
                    var remoteMatch = remoteChildren.FirstOrDefault(c => c.Name == name);

                    var content = await File.ReadAllBytesAsync(entry);
                    var etagFile = entry + ".remote-etag";
                    var localEtag = File.Exists(etagFile) ? await File.ReadAllTextAsync(etagFile) : null;

                    if (remoteMatch == null || localEtag == null)
                    {
                        var uploaded = await UploadFileAsync(remoteFolderId, name, content, driveId);
                        if (uploaded != null) await File.WriteAllTextAsync(etagFile, uploaded.ETag);
                    }
                }
            }

            // Mark as fully synched locally
            await File.WriteAllTextAsync(Path.Combine(localPath, ".synched"), "");
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
