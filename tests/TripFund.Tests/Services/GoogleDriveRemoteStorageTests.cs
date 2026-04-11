using System.Net;
using System.Text.Json;
using Moq;
using TripFund.App.Models;
using TripFund.App.Services;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using FluentAssertions;
using WireMock.Matchers;

namespace TripFund.Tests.Services;

public class GoogleDriveRemoteStorageTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly string _tempPath;
    private readonly Mock<TripFund.App.Services.IWebAuthenticator> _mockAuthenticator;
    private readonly GoogleDriveRemoteStorageService _service;
    private readonly LocalTripStorageService _localStorage;
    private readonly HttpClient _httpClient;

    public GoogleDriveRemoteStorageTests()
    {
        _server = WireMockServer.Start();
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);

        _mockAuthenticator = new Mock<TripFund.App.Services.IWebAuthenticator>();
        _localStorage = new LocalTripStorageService(_tempPath);
        _httpClient = new HttpClient { BaseAddress = new Uri(_server.Urls[0]) };
        
        _service = new GoogleDriveRemoteStorageService(_httpClient, _mockAuthenticator.Object, _localStorage);
    }

    public void Dispose()
    {
        _server.Stop();
        if (Directory.Exists(_tempPath)) Directory.Delete(_tempPath, true);
    }

    private void SetupAuth()
    {
        _mockAuthenticator.Setup(a => a.AuthenticateAsync(It.IsAny<WebAuthenticatorOptions>()))
            .ReturnsAsync(new WebAuthenticatorResult(new Dictionary<string, string> { { "code", "fake_code" } }));

        _server.Given(Request.Create().WithPath("/oauth2/v4/token").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(new
                {
                    access_token = "fake_access_token",
                    refresh_token = "fake_refresh_token",
                    expires_in = 3600
                })));
    }

    [Fact]
    public async Task GetRemoteTripConfigAsync_ShouldReturnConfig_WhenValidUrl()
    {
        // Arrange
        SetupAuth();
        var folderId = "folder123";
        var url = $"https://drive.google.com/drive/folders/{folderId}";
        
        // Mock GetChildFolderId (metadata)
        _server.Given(Request.Create().WithPath("/drive/v3/files").WithParam("q", new WildcardMatcher("*'folder123' in parents*metadata*")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(new { files = new[] { new { id = "metaId", name = "metadata" } } })));

        // Mock GetLatestVersionFolderId (001_new_dev)
        _server.Given(Request.Create().WithPath("/drive/v3/files").WithParam("q", new WildcardMatcher("*'metaId' in parents*")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(new { files = new[] { new { id = "v1Id", name = "001_new_dev1" } } })));

        // Mock DownloadFileContent (trip_config.json)
        var expectedConfig = new TripConfig { Id = "trip1", Name = "Test Trip" };
        _server.Given(Request.Create().WithPath("/drive/v3/files").WithParam("q", new WildcardMatcher("*'v1Id' in parents*trip_config.json*")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(new { files = new[] { new { id = "fileId", name = "trip_config.json" } } })));

        _server.Given(Request.Create().WithPath("/drive/v3/files/fileId").WithParam("alt", "media").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(expectedConfig)));

        // Act
        var config = await _service.GetRemoteTripConfigAsync("google-drive", new Dictionary<string, string> { { "folderUrl", url } });

        // Assert
        config.Should().NotBeNull();
        config!.Name.Should().Be("Test Trip");
    }

    [Fact]
    public async Task SynchronizeAsync_ShouldSetReadonly_WhenWriteFails()
    {
        // Arrange
        SetupAuth();
        var tripSlug = "my-trip";
        var folderId = "rootId";
        var deviceId = "test-device";
        
        await _localStorage.SaveAppSettingsAsync(new AppSettings { DeviceId = deviceId });
        
        var registry = new LocalTripRegistry();
        registry.Trips[tripSlug] = new TripRegistryEntry
        {
            RemoteStorage = new RemoteStorageConfig
            {
                Provider = "google-drive",
                Parameters = new Dictionary<string, string> { { "folderUrl", $"https://drive.google.com/drive/folders/{folderId}" } }
            }
        };
        await _localStorage.SaveTripRegistryAsync(registry);

        // Mock permission check: list returns nothing, write fails
        _server.Given(Request.Create().WithPath("/drive/v3/files").WithParam("q", new WildcardMatcher($"*name = '.rw-test-{deviceId}'*")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(new { files = new DriveItem[] { } })));

        _server.Given(Request.Create().WithPath("/upload/drive/v3/files").WithParam("uploadType", "multipart").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(403));

        // Mock empty sync response to avoid other failures
        _server.Given(Request.Create().WithPath("/drive/v3/files").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(new { files = new DriveItem[] { } })));

        // Act
        await _service.SynchronizeAsync(tripSlug);

        // Assert
        var updatedRegistry = await _localStorage.GetTripRegistryAsync();
        updatedRegistry.Trips[tripSlug].RemoteStorage!.Readonly.Should().BeTrue();
    }

    [Fact]
    public async Task SynchronizeAsync_ShouldBeWritable_WhenExistingTestFileIsDeleted()
    {
        // Arrange
        SetupAuth();
        var tripSlug = "my-trip";
        var folderId = "rootId";
        var deviceId = "test-device";
        var existingFileId = "existingId";
        
        await _localStorage.SaveAppSettingsAsync(new AppSettings { DeviceId = deviceId });
        
        var registry = new LocalTripRegistry();
        registry.Trips[tripSlug] = new TripRegistryEntry
        {
            RemoteStorage = new RemoteStorageConfig
            {
                Provider = "google-drive",
                Parameters = new Dictionary<string, string> { { "folderUrl", $"https://drive.google.com/drive/folders/{folderId}" } }
            }
        };
        await _localStorage.SaveTripRegistryAsync(registry);

        // Mock permission check: list returns existing, delete succeeds
        _server.Given(Request.Create().WithPath("/drive/v3/files").WithParam("q", new WildcardMatcher($"*name = '.rw-test-{deviceId}'*")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(new { files = new[] { new { id = existingFileId, name = $".rw-test-{deviceId}" } } })));

        _server.Given(Request.Create().WithPath($"/drive/v3/files/{existingFileId}").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(204));

        // Mock metadata folder (for sync down)
        _server.Given(Request.Create().WithPath("/drive/v3/files").WithParam("q", new WildcardMatcher("*'rootId' in parents*metadata*")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(new { files = new DriveItem[] { } })));

        // Mock transactions folder (for sync down)
        _server.Given(Request.Create().WithPath("/drive/v3/files").WithParam("q", new WildcardMatcher("*'rootId' in parents*transactions*")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(new { files = new DriveItem[] { } })));

        // Act
        await _service.SynchronizeAsync(tripSlug);

        // Assert
        var updatedRegistry = await _localStorage.GetTripRegistryAsync();
        updatedRegistry.Trips[tripSlug].RemoteStorage!.Readonly.Should().BeFalse();
    }

    [Fact]
    public async Task SynchronizeAsync_ShouldDownloadMissingLocalVersions()
    {
        // Arrange
        SetupAuth();
        var tripSlug = "my-trip";
        var folderId = "rootId";
        var deviceId = "test-device";
        
        await _localStorage.SaveAppSettingsAsync(new AppSettings { DeviceId = deviceId });
        
        var registry = new LocalTripRegistry();
        registry.Trips[tripSlug] = new TripRegistryEntry
        {
            RemoteStorage = new RemoteStorageConfig
            {
                Provider = "google-drive",
                Parameters = new Dictionary<string, string> { { "folderUrl", $"https://drive.google.com/drive/folders/{folderId}" } }
            }
        };
        await _localStorage.SaveTripRegistryAsync(registry);

        // Mock permission check success (create and delete)
        _server.Given(Request.Create().WithPath("/drive/v3/files").WithParam("q", new WildcardMatcher($"*name = '.rw-test-{deviceId}'*")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(new { files = new DriveItem[] { } })));

        _server.Given(Request.Create().WithPath("/upload/drive/v3/files").WithParam("uploadType", "multipart").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(new { id = "testFileId" })));
        
        _server.Given(Request.Create().WithPath("/drive/v3/files/testFileId").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(204));

        // Mock metadata folder
        _server.Given(Request.Create().WithPath("/drive/v3/files").WithParam("q", new WildcardMatcher("*'rootId' in parents*metadata*")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(new { files = new[] { new { id = "metaId", name = "metadata" } } })));

        // Mock metadata versions (001_new_dev1)
        _server.Given(Request.Create().WithPath("/drive/v3/files").WithParam("q", new WildcardMatcher("*'metaId' in parents*")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(new { files = new[] { new { id = "v1Id", name = "001_new_dev1" } } })));

        // Mock v1Id content listing
        _server.Given(Request.Create().WithPath("/drive/v3/files").WithParam("q", new WildcardMatcher("*'v1Id' in parents*")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(new { files = new[] { new { id = "f1Id", name = "trip_config.json" } } })));

        // Mock file download
        _server.Given(Request.Create().WithPath("/drive/v3/files/f1Id").WithParam("alt", "media").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(new TripConfig { Name = "Downloaded Trip" })));

        // Mock transactions (empty for now)
        _server.Given(Request.Create().WithPath("/drive/v3/files").WithParam("q", new WildcardMatcher("*'rootId' in parents*transactions*")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(new { files = new DriveItem[] { } })));

        // Act
        await _service.SynchronizeAsync(tripSlug);

        // Assert
        var localPath = Path.Combine(_tempPath, "trips", tripSlug, "metadata", "001_new_dev1", "trip_config.json");
        File.Exists(localPath).Should().BeTrue();
        
        var updatedRegistry = await _localStorage.GetTripRegistryAsync();
        updatedRegistry.Trips[tripSlug].RemoteStorage!.Readonly.Should().BeFalse();
    }

    private class DriveItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }
}
