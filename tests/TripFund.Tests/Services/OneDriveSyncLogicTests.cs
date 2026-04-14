using IWebAuthenticator = TripFund.App.Services.IWebAuthenticator;
using System.Net;
using Moq;
using TripFund.App.Models;
using TripFund.App.Services;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace TripFund.Tests.Services;

public class OneDriveSyncLogicTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly Mock<IWebAuthenticator> _authenticatorMock;
    private readonly Mock<IMicrosoftAuthConfiguration> _configMock;
    private readonly OneDriveRemoteStorageService _service;
    private readonly string _tempPath;

    public OneDriveSyncLogicTests()
    {
        _server = WireMockServer.Start();
        _authenticatorMock = new Mock<IWebAuthenticator>();
        _configMock = new Mock<IMicrosoftAuthConfiguration>();
        
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);
        
        var localStorage = new LocalTripStorageService(_tempPath);
        
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var client = new HttpClient { BaseAddress = new Uri(_server.Urls[0]) };
        httpClientFactoryMock.Setup(f => f.CreateClient(nameof(OneDriveRemoteStorageService))).Returns(client);

        var syncEngine = new RemoteStorageSyncEngine(localStorage);

        _service = new OneDriveRemoteStorageService(
            httpClientFactoryMock.Object,
            _authenticatorMock.Object,
            localStorage,
            _configMock.Object,
            syncEngine,
            _server.Urls[0]);

        // Mock Authentication
        _authenticatorMock.Setup(a => a.AuthenticateAsync(It.IsAny<WebAuthenticatorOptions>()))
            .ReturnsAsync(new WebAuthenticatorResult());
    }

    [Fact]
    public async Task GetRemoteTripConfigAsync_CorrectlyLoadsConfig_WhenGivenTripRootId()
    {
        var parameters = new Dictionary<string, string> { { "folderId", "root_id" }, { "accessToken", "fake_token" }, { "accessTokenExpiry", DateTime.Now.AddHours(1).ToString("O") } };
        
        // 1. Mock GetChildItemAsync(root_id, "metadata")
        _server.Given(Request.Create().WithPath("/me/drive/items/root_id:/metadata").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"id\": \"meta_id\", \"name\": \"metadata\", \"folder\": {} }"));

        // 2. Mock ListChildrenAsync(meta_id)
        _server.Given(Request.Create().WithPath("/me/drive/items/meta_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [ { \"id\": \"v1_id\", \"name\": \"001_NEW_dev1\", \"folder\": {} } ] }"));

        // 3. Mock GetChildItemAsync(v1_id, "trip_config.json")
        _server.Given(Request.Create().WithPath("/me/drive/items/v1_id:/trip_config.json").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"id\": \"file_id\", \"name\": \"trip_config.json\", \"file\": {} }"));

        // 4. Mock DownloadFileContentAsync(file_id)
        var tripConfig = new TripConfig { Name = "Test Trip", StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(7) };
        var json = System.Text.Json.JsonSerializer.Serialize(tripConfig);
        _server.Given(Request.Create().WithPath("/me/drive/items/file_id/content").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(json));

        // Act
        var result = await _service.GetRemoteTripConfigAsync("onedrive", parameters);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test Trip", result.Name);
    }

    [Fact]
    public async Task SyncDown_RestartsCopy_IfLocalFolderHasSynchingFile()
    {
        // Arrange
        var tripSlug = "test-trip";
        var localTripPath = Path.Combine(_tempPath, "trips", tripSlug);
        var leafPath = Path.Combine(localTripPath, "metadata", "001_NEW_dev1");
        Directory.CreateDirectory(leafPath);
        File.WriteAllText(Path.Combine(leafPath, ".synching.tf"), "");
        File.WriteAllText(Path.Combine(leafPath, "old_file.json"), "old content");

        // Mock OneDrive responses
        // 1. List metadata children
        _server.Given(Request.Create().WithPath("/me/drive/items/root_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [ { \"id\": \"meta_id\", \"name\": \"metadata\", \"folder\": {} } ] }"));

        // 2. List 001_NEW_dev1 children
        _server.Given(Request.Create().WithPath("/me/drive/items/meta_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [ { \"id\": \"v1_id\", \"name\": \"001_NEW_dev1\", \"folder\": {} } ] }"));

        // 3. List files in v1_id (Leaf folder)
        _server.Given(Request.Create().WithPath("/me/drive/items/v1_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [ { \"id\": \"file1_id\", \"name\": \"trip_config.json\", \"file\": {}, \"eTag\": \"etag1\" } ] }"));

        // 4. Download content
        _server.Given(Request.Create().WithPath("/me/drive/items/file1_id/content").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"TripName\": \"Test Trip\" }"));

        var registry = new LocalTripRegistry();
        registry.Trips[tripSlug] = new TripRegistryEntry 
        { 
            RemoteStorage = new RemoteStorageConfig 
            { 
                Provider = "onedrive", 
                Parameters = new Dictionary<string, string> 
                { 
                    { "folderId", "root_id" },
                    { "accessToken", "fake_token" },
                    { "accessTokenExpiry", DateTime.Now.AddHours(1).ToString("O") }
                } 
            } 
        };
        var localStorage = new LocalTripStorageService(_tempPath);
        await localStorage.SaveTripRegistryAsync(registry);

        // We also need to mock the write test
        _server.Given(Request.Create().WithPath("/me/drive/items/root_id:/ .rw-test-unknown-device").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
        _server.Given(Request.Create().WithPath("/me/drive/items/root_id/children").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithBody("{ \"id\": \"test_file_id\" }"));
        _server.Given(Request.Create().WithPath("/me/drive/items/test_file_id").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(204));

        await _service.SynchronizeAsync(tripSlug);

        // Assert
        Assert.True(File.Exists(Path.Combine(leafPath, "trip_config.json")));
        Assert.False(File.Exists(Path.Combine(leafPath, ".synching.tf")));
        Assert.True(File.Exists(Path.Combine(leafPath, ".synched.tf"))); // Optimization marker
        Assert.False(File.Exists(Path.Combine(leafPath, "old_file.json"))); // Should have been cleared
    }

    [Fact]
    public async Task SyncDown_Throws_IfMixedFolderFoundOnRemote()
    {
        // Arrange
        var tripSlug = "test-trip";
        var registry = new LocalTripRegistry();
        registry.Trips[tripSlug] = new TripRegistryEntry 
        { 
            RemoteStorage = new RemoteStorageConfig 
            { 
                Provider = "onedrive", 
                Parameters = new Dictionary<string, string> 
                { 
                    { "folderId", "root_id" },
                    { "accessToken", "fake_token" },
                    { "accessTokenExpiry", DateTime.Now.AddHours(1).ToString("O") }
                } 
            } 
        };
        var localStorage = new LocalTripStorageService(_tempPath);
        await localStorage.SaveTripRegistryAsync(registry);

        // Mock OneDrive responses: Root folder has both a folder and a file
        _server.Given(Request.Create().WithPath("/me/drive/items/root_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [ { \"id\": \"meta_id\", \"name\": \"metadata\", \"folder\": {} }, { \"id\": \"file_id\", \"name\": \"oops.txt\", \"file\": {} } ] }"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SynchronizeAsync(tripSlug));
    }

    [Fact]
    public async Task SyncDown_SkipsRemoteList_IfLocalFolderHasSynchedFile()
    {
        // Arrange
        var tripSlug = "test-trip";
        var localTripPath = Path.Combine(_tempPath, "trips", tripSlug);
        var leafPath = Path.Combine(localTripPath, "metadata", "001_NEW_dev1");
        Directory.CreateDirectory(leafPath);
        File.WriteAllText(Path.Combine(leafPath, ".synched.tf"), "");
        File.WriteAllText(Path.Combine(leafPath, "trip_config.json"), "{}");

        var registry = new LocalTripRegistry();
        registry.Trips[tripSlug] = new TripRegistryEntry 
        { 
            RemoteStorage = new RemoteStorageConfig 
            { 
                Provider = "onedrive", 
                Parameters = new Dictionary<string, string> 
                { 
                    { "folderId", "root_id" },
                    { "accessToken", "fake_token" },
                    { "accessTokenExpiry", DateTime.Now.AddHours(1).ToString("O") }
                } 
            } 
        };
        var localStorage = new LocalTripStorageService(_tempPath);
        await localStorage.SaveTripRegistryAsync(registry);

        // Mock write test (SynchronizeAsync always calls this first)
        _server.Given(Request.Create().WithPath("/me/drive/items/root_id:/ .rw-test-unknown-device").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
        _server.Given(Request.Create().WithPath("/me/drive/items/root_id/children").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithBody("{ \"id\": \"test_file_id\" }"));
        _server.Given(Request.Create().WithPath("/me/drive/items/test_file_id").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(204));

        // We EXPECT that metadata children WILL be listed (because metadata/ is a Node folder)
        // BUT 001_NEW_dev1 children WILL NOT be listed!
        _server.Given(Request.Create().WithPath("/me/drive/items/root_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [ { \"id\": \"meta_id\", \"name\": \"metadata\", \"folder\": {} } ] }"));

        _server.Given(Request.Create().WithPath("/me/drive/items/meta_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [ { \"id\": \"v1_id\", \"name\": \"001_NEW_dev1\", \"folder\": {} } ] }"));

        // NO mapping for /me/drive/items/v1_id/children should be hit!
        
        // Act
        await _service.SynchronizeAsync(tripSlug);

        // Assert
        var requests = _server.FindLogEntries(Request.Create().WithPath("/me/drive/items/v1_id/children"));
        Assert.Empty(requests);
    }

    public void Dispose()
    {
        _server.Stop();
        if (Directory.Exists(_tempPath)) Directory.Delete(_tempPath, true);
    }
}
