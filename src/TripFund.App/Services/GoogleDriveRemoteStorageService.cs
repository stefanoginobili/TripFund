using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TripFund.App.Models;

namespace TripFund.App.Services;

public class GoogleDriveRemoteStorageService : IRemoteStorageService
{
    private readonly HttpClient _httpClient;
    private readonly IWebAuthenticator _authenticator;
    private readonly LocalTripStorageService _localStorage;
    private readonly VersionedStorageEngine _engine = new();
    private string? _accessToken;
    private string? _refreshToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public GoogleDriveRemoteStorageService(
        HttpClient httpClient, 
        IWebAuthenticator authenticator, 
        LocalTripStorageService localStorage)
    {
        _httpClient = httpClient;
        _authenticator = authenticator;
        _localStorage = localStorage;
    }

    public async Task<TripConfig?> GetRemoteTripConfigAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider != "google-drive" || !parameters.TryGetValue("folderUrl", out var url)) return null;

        var folderId = GetFolderIdFromUrl(url);
        if (string.IsNullOrEmpty(folderId)) return null;

        await EnsureAuthenticatedAsync();

        // 2. Look for metadata/trip_config.json
        var metadataFolderId = await GetChildFolderIdAsync(folderId, "metadata");
        if (string.IsNullOrEmpty(metadataFolderId)) return null;

        var latestVersionId = await GetLatestVersionFolderIdAsync(metadataFolderId);
        if (string.IsNullOrEmpty(latestVersionId)) return null;

        var configJson = await DownloadFileContentAsync(latestVersionId, "trip_config.json");
        if (configJson == null) return null;

        return JsonSerializer.Deserialize<TripConfig>(configJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public async Task<bool> IsRemoteLocationEmptyAsync(string provider, Dictionary<string, string> parameters)
    {
        if (provider != "google-drive" || !parameters.TryGetValue("folderUrl", out var url)) return false;

        var folderId = GetFolderIdFromUrl(url);
        if (string.IsNullOrEmpty(folderId)) return false;

        await EnsureAuthenticatedAsync();

        var files = await ListDriveFilesAsync(folderId);
        return files.Count == 0;
    }

    public async Task SynchronizeAsync(string tripSlug)
    {
        var registry = await _localStorage.GetTripRegistryAsync();
        if (!registry.Trips.TryGetValue(tripSlug, out var entry) || entry.RemoteStorage == null) return;

        var folderUrl = entry.RemoteStorage.Parameters.GetValueOrDefault("folderUrl");
        if (string.IsNullOrEmpty(folderUrl)) return;

        var folderId = GetFolderIdFromUrl(folderUrl);
        if (string.IsNullOrEmpty(folderId)) return;

        await EnsureAuthenticatedAsync();

        // Check if root is readonly by trying to write/delete a test file
        var canWrite = await CheckFolderWritePermissionAsync(folderId);
        if (entry.RemoteStorage.Readonly != !canWrite)
        {
            entry.RemoteStorage.Readonly = !canWrite;
            await _localStorage.SaveTripRegistryAsync(registry);
        }

        var localTripPath = Path.Combine(_localStorage.TripsPath, tripSlug);

        // 1. Download Phase
        await SyncDownAsync(folderId, localTripPath);

        // 2. Integrity & Conflict Check
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
            await SyncUpAsync(localTripPath, folderId);
        }
    }

    private async Task<bool> CheckFolderWritePermissionAsync(string folderId)
    {
        try
        {
            var settings = await _localStorage.GetAppSettingsAsync();
            var deviceId = settings?.DeviceId ?? "unknown-device";
            var fileName = $".rw-test-{deviceId}";
            
            // Check if test file already exists
            var existingFiles = await ListDriveFilesAsync(folderId, $"name = '{fileName}'");
            if (existingFiles.Any())
            {
                // Try to delete existing file to confirm write permission
                try
                {
                    await DeleteDriveFileAsync(existingFiles.First().Id);
                    return true;
                }
                catch
                {
                    // If deletion of existing fails, proceed to try creation
                }
            }

            // Try to create/upload a new test file
            var fileId = await CreateDriveFileAsync(folderId, fileName, new byte[] { 0x01 });
            if (string.IsNullOrEmpty(fileId)) return false;
            
            // Try to delete it
            await DeleteDriveFileAsync(fileId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task SyncDownAsync(string remoteRootId, string localRootPath)
    {
        var remoteMetadataId = await GetChildFolderIdAsync(remoteRootId, "metadata");
        if (!string.IsNullOrEmpty(remoteMetadataId))
        {
            await SyncFolderDownAsync(remoteMetadataId, Path.Combine(localRootPath, "metadata"));
        }

        var remoteTransId = await GetChildFolderIdAsync(remoteRootId, "transactions");
        if (!string.IsNullOrEmpty(remoteTransId))
        {
            var remoteTransactions = await ListDriveFilesAsync(remoteTransId, "mimeType = 'application/vnd.google-apps.folder'");
            foreach (var rt in remoteTransactions)
            {
                await SyncFolderDownAsync(rt.Id, Path.Combine(localRootPath, "transactions", rt.Name));
            }
        }
    }

    private async Task SyncFolderDownAsync(string remoteFolderId, string localFolderPath)
    {
        var remoteVersions = await ListDriveFilesAsync(remoteFolderId, "mimeType = 'application/vnd.google-apps.folder'");
        foreach (var rv in remoteVersions)
        {
            if (rv.Name.EndsWith(".synching")) continue;

            var targetLocalPath = Path.Combine(localFolderPath, rv.Name);
            if (!Directory.Exists(targetLocalPath))
            {
                // Atomic Download
                var tempPath = targetLocalPath + ".synching";
                if (Directory.Exists(tempPath)) Directory.Delete(tempPath, true);
                Directory.CreateDirectory(tempPath);

                var files = await ListDriveFilesAsync(rv.Id);
                foreach (var f in files)
                {
                    var content = await DownloadFileContentAsync(f.Id);
                    if (content != null)
                    {
                        await File.WriteAllBytesAsync(Path.Combine(tempPath, f.Name), content);
                    }
                }

                Directory.Move(tempPath, targetLocalPath);
            }
        }
    }

    private async Task SyncUpAsync(string localRootPath, string remoteRootId)
    {
        var metadataPath = Path.Combine(localRootPath, "metadata");
        if (Directory.Exists(metadataPath))
        {
            var remoteMetadataId = await GetOrCreateChildFolderAsync(remoteRootId, "metadata");
            await SyncFolderUpAsync(metadataPath, remoteMetadataId);
        }

        var transPath = Path.Combine(localRootPath, "transactions");
        if (Directory.Exists(transPath))
        {
            var remoteTransId = await GetOrCreateChildFolderAsync(remoteRootId, "transactions");
            foreach (var lt in Directory.GetDirectories(transPath))
            {
                var transId = Path.GetFileName(lt);
                var remoteTId = await GetOrCreateChildFolderAsync(remoteTransId, transId);
                await SyncFolderUpAsync(lt, remoteTId);
            }
        }
    }

    private async Task SyncFolderUpAsync(string localPath, string remoteFolderId)
    {
        var localVersions = Directory.GetDirectories(localPath).Select(Path.GetFileName).ToList();
        var remoteVersions = (await ListDriveFilesAsync(remoteFolderId, "mimeType = 'application/vnd.google-apps.folder'"))
            .Select(f => f.Name).ToList();

        foreach (var lv in localVersions)
        {
            if (lv == null || lv.EndsWith(".synching") || remoteVersions.Contains(lv)) continue;

            // Atomic Upload
            var remoteTempId = await CreateDriveFolderAsync(remoteFolderId, lv + ".synching");
            var files = Directory.GetFiles(Path.Combine(localPath, lv));
            foreach (var f in files)
            {
                await UploadDriveFileAsync(remoteTempId, Path.GetFileName(f), await File.ReadAllBytesAsync(f));
            }

            await RenameDriveFolderAsync(remoteTempId, lv);
        }
    }

    // --- Private Google Drive Helpers ---

    private string? GetFolderIdFromUrl(string url)
    {
        var match = Regex.Match(url, @"folders/([a-zA-Z0-9_-]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_accessToken != null && DateTime.UtcNow < _tokenExpiry) return;

        if (_refreshToken != null)
        {
            await RefreshAccessTokenAsync();
            return;
        }

        var callbackUrl = "com.stefanoginobili.tripfund.app:/oauth2redirect";
        var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?client_id={Config.GoogleClientId}&response_type=code&scope=https://www.googleapis.com/auth/drive.file&redirect_uri={Uri.EscapeDataString(callbackUrl)}";
        
        var result = await _authenticator.AuthenticateAsync(new WebAuthenticatorOptions
        {
            Url = new Uri(authUrl),
            CallbackUrl = new Uri(callbackUrl)
        });

        if (result != null && result.Properties.TryGetValue("code", out var code))
        {
            await ExchangeAuthCodeAsync(code);
        }
    }

    private async Task ExchangeAuthCodeAsync(string code)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("client_id", Config.GoogleClientId),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("redirect_uri", "com.stefanoginobili.tripfund.app:/oauth2redirect")
        });

        var response = await _httpClient.PostAsync("/oauth2/v4/token", content);
        if (response.IsSuccessStatusCode)
        {
            var tokenResp = await response.Content.ReadFromJsonAsync<TokenResponse>();
            if (tokenResp != null)
            {
                _accessToken = tokenResp.AccessToken;
                _refreshToken = tokenResp.RefreshToken;
                _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResp.ExpiresIn - 60);
            }
        }
    }

    private async Task RefreshAccessTokenAsync()
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("refresh_token", _refreshToken ?? ""),
            new KeyValuePair<string, string>("client_id", Config.GoogleClientId),
            new KeyValuePair<string, string>("grant_type", "refresh_token")
        });

        var response = await _httpClient.PostAsync("/oauth2/v4/token", content);
        if (response.IsSuccessStatusCode)
        {
            var tokenResp = await response.Content.ReadFromJsonAsync<TokenResponse>();
            if (tokenResp != null)
            {
                _accessToken = tokenResp.AccessToken;
                _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResp.ExpiresIn - 60);
            }
        }
    }

    private async Task<DriveItem?> GetDriveItemAsync(string id)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        var response = await _httpClient.GetAsync($"/drive/v3/files/{id}?fields=id,name,mimeType,capabilities");
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<DriveItem>();
        }
        return null;
    }

    private async Task<List<DriveItem>> ListDriveFilesAsync(string parentId, string? qExtra = null)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        var q = $"'{parentId}' in parents and trashed = false";
        if (qExtra != null) q += $" and {qExtra}";
        
        var response = await _httpClient.GetAsync($"/drive/v3/files?q={Uri.EscapeDataString(q)}&fields=files(id,name,mimeType)");
        if (response.IsSuccessStatusCode)
        {
            var list = await response.Content.ReadFromJsonAsync<DriveListResponse>();
            return list?.Files ?? new List<DriveItem>();
        }
        return new List<DriveItem>();
    }

    private async Task<string?> GetChildFolderIdAsync(string parentId, string name)
    {
        var files = await ListDriveFilesAsync(parentId, $"name = '{name}' and mimeType = 'application/vnd.google-apps.folder'");
        return files.FirstOrDefault()?.Id;
    }

    private async Task<string> GetOrCreateChildFolderAsync(string parentId, string name)
    {
        var id = await GetChildFolderIdAsync(parentId, name);
        if (id != null) return id;
        return await CreateDriveFolderAsync(parentId, name);
    }

    private async Task<string> CreateDriveFolderAsync(string parentId, string name)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        var body = new { name = name, mimeType = "application/vnd.google-apps.folder", parents = new[] { parentId } };
        var response = await _httpClient.PostAsJsonAsync("/drive/v3/files", body);
        var item = await response.Content.ReadFromJsonAsync<DriveItem>();
        return item?.Id ?? throw new Exception("Failed to create folder");
    }

    private async Task<string> CreateDriveFileAsync(string parentId, string name, byte[] content)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        
        var metadata = new { name = name, parents = new[] { parentId } };
        var multipartContent = new MultipartFormDataContent();
        
        var metaContent = new StringContent(JsonSerializer.Serialize(metadata));
        metaContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        multipartContent.Add(metaContent, "metadata");

        var byteContent = new ByteArrayContent(content);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipartContent.Add(byteContent, "media");

        var response = await _httpClient.PostAsync("/upload/drive/v3/files?uploadType=multipart", multipartContent);
        if (response.IsSuccessStatusCode)
        {
            var item = await response.Content.ReadFromJsonAsync<DriveItem>();
            return item?.Id ?? "";
        }
        return "";
    }

    private async Task DeleteDriveFileAsync(string fileId)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        var response = await _httpClient.DeleteAsync($"/drive/v3/files/{fileId}");
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception("Failed to delete file");
        }
    }

    private async Task<string?> GetLatestVersionFolderIdAsync(string folderId)
    {
        var folders = await ListDriveFilesAsync(folderId, "mimeType = 'application/vnd.google-apps.folder'");
        return folders.OrderByDescending(f => f.Name).FirstOrDefault()?.Id;
    }

    private async Task<byte[]?> DownloadFileContentAsync(string folderId, string? fileName = null)
    {
        string fileId = folderId;
        if (fileName != null)
        {
            var files = await ListDriveFilesAsync(folderId, $"name = '{fileName}'");
            if (!files.Any()) return null;
            fileId = files.First().Id;
        }

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        var response = await _httpClient.GetAsync($"/drive/v3/files/{fileId}?alt=media");
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsByteArrayAsync();
        }
        return null;
    }

    private async Task UploadDriveFileAsync(string parentId, string name, byte[] content)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        
        var metadata = new { name = name, parents = new[] { parentId } };
        var multipartContent = new MultipartFormDataContent();
        
        var metaContent = new StringContent(JsonSerializer.Serialize(metadata));
        metaContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        multipartContent.Add(metaContent, "metadata");

        var byteContent = new ByteArrayContent(content);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipartContent.Add(byteContent, "media");

        await _httpClient.PostAsync("/upload/drive/v3/files?uploadType=multipart", multipartContent);
    }

    private async Task RenameDriveFolderAsync(string folderId, string newName)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        var body = new { name = newName };
        await _httpClient.PatchAsJsonAsync($"/drive/v3/files/{folderId}", body);
    }

    // --- API Models ---

    private class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    private class DriveItem
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("mimeType")] public string MimeType { get; set; } = "";
        [JsonPropertyName("capabilities")] public DriveCapabilities? Capabilities { get; set; }
    }

    private class DriveCapabilities
    {
        [JsonPropertyName("canEdit")] public bool CanEdit { get; set; }
    }

    private class DriveListResponse
    {
        [JsonPropertyName("files")] public List<DriveItem> Files { get; set; } = new();
    }
}
