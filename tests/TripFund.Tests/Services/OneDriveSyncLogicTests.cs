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
        
        // 1. Mock GetChildItemAsync(root_id, "config_versioned")
        _server.Given(Request.Create().WithPath("/me/drive/items/root_id:/config_versioned").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"id\": \"config_versioned_id\", \"name\": \"config_versioned\", \"folder\": {} }"));

        // 2. Mock ListChildrenAsync(config_versioned_id)
        _server.Given(Request.Create().WithPath("/me/drive/items/config_versioned_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [ { \"id\": \"v1_id\", \"name\": \"001_NEW_dev1\", \"folder\": {} } ] }"));

        // 3. Mock GetChildItemAsync(v1_id, ".data")
        _server.Given(Request.Create().WithPath("/me/drive/items/v1_id:/.data").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"id\": \"data_id\", \"name\": \".data\", \"folder\": {} }"));

        // 4. Mock GetChildItemAsync(data_id, "trip_config.json")
        _server.Given(Request.Create().WithPath("/me/drive/items/data_id:/trip_config.json").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"id\": \"file_id\", \"name\": \"trip_config.json\", \"file\": {} }"));

        // 5. Mock DownloadFileContentAsync(file_id)
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
    public async Task SyncDown_RestartsCopy_IfLocalFolderHasDownloadingMarker()
    {
        // Arrange
        var tripSlug = "test-trip";
        var localTripPath = Path.Combine(_tempPath, "trips", tripSlug);
        var leafPath = Path.Combine(localTripPath, "config_versioned", "001_NEW_dev1");
        Directory.CreateDirectory(leafPath);
        
        var localLeaf = new LocalLeafFolder(leafPath);
        await localLeaf.WriteMarkerAsync(".downloading", "begin=2023-10-01T12:00:00Z");
        await localLeaf.WriteDataFileAsync("old_file.json", System.Text.Encoding.UTF8.GetBytes("old content"));

        // Mock OneDrive responses
        // 1. List config_versioned children
        _server.Given(Request.Create().WithPath("/me/drive/items/root_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [ { \"id\": \"config_versioned_id\", \"name\": \"config_versioned\", \"folder\": {} } ] }"));

        // 2. List 001_NEW_dev1 children (to detect leaf)
        _server.Given(Request.Create().WithPath("/me/drive/items/config_versioned_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [ { \"id\": \"v1_id\", \"name\": \"001_NEW_dev1\", \"folder\": {} } ] }"));

        // 3. List children of v1_id (Leaf folder) -> Has .metadata and .data
        _server.Given(Request.Create().WithPath("/me/drive/items/v1_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [ { \"id\": \"meta_id\", \"name\": \".metadata\", \"file\": {} }, { \"id\": \"data_id\", \"name\": \".data\", \"folder\": {} } ] }"));

        // 4. Download .metadata
        _server.Given(Request.Create().WithPath("/me/drive/items/v1_id:/.metadata").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"id\": \"meta_id\", \"name\": \".metadata\", \"file\": {} }"));
        _server.Given(Request.Create().WithPath("/me/drive/items/meta_id/content").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("author=mario\ndevice=dev1\ntimestamp=2023-10-01T12:00:00Z"));

        // 5. Get .data folder
        _server.Given(Request.Create().WithPath("/me/drive/items/v1_id:/.data").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"id\": \"data_id\", \"name\": \".data\", \"folder\": {} }"));

        // 6. List children of .data
        _server.Given(Request.Create().WithPath("/me/drive/items/data_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [ { \"id\": \"file1_id\", \"name\": \"trip_config.json\", \"file\": {} } ] }"));

        // 6b. Get trip_config.json in .data
        _server.Given(Request.Create().WithPath("/me/drive/items/data_id:/trip_config.json").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"id\": \"file1_id\", \"name\": \"trip_config.json\", \"file\": {} }"));

        // 7. Download trip_config.json
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

        // Mock write test
        _server.Given(Request.Create().WithPath("/me/drive/items/root_id:/ .rw-test-unknown-device").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
        _server.Given(Request.Create().WithPath("/me/drive/items/root_id/children").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithBody("{ \"id\": \"test_file_id\" }"));
        _server.Given(Request.Create().WithPath("/me/drive/items/test_file_id").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(204));

        await _service.SynchronizeAsync(tripSlug);

        // Assert
        Assert.True(File.Exists(Path.Combine(leafPath, ".data", "trip_config.json")));
        Assert.False(File.Exists(Path.Combine(leafPath, ".downloading")));
        Assert.True(File.Exists(Path.Combine(leafPath, ".synched")));
        Assert.False(File.Exists(Path.Combine(leafPath, ".data", "old_file.json"))); // Should have been cleared
    }

    [Fact]
    public async Task SyncDown_SkipsRemoteList_IfLocalFolderHasSynchedMarker()
    {
        // Arrange
        var tripSlug = "test-trip";
        var localTripPath = Path.Combine(_tempPath, "trips", tripSlug);
        var leafPath = Path.Combine(localTripPath, "config_versioned", "001_NEW_dev1");
        Directory.CreateDirectory(leafPath);
        File.WriteAllText(Path.Combine(leafPath, ".synched"), "");
        Directory.CreateDirectory(Path.Combine(leafPath, ".data"));
        File.WriteAllText(Path.Combine(leafPath, ".data", "trip_config.json"), "{}");
        File.WriteAllText(Path.Combine(leafPath, ".metadata"), "author=mario");

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

        _server.Given(Request.Create().WithPath("/me/drive/items/root_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [ { \"id\": \"config_versioned_id\", \"name\": \"config_versioned\", \"folder\": {} } ] }"));

        _server.Given(Request.Create().WithPath("/me/drive/items/config_versioned_id/children").UsingGet())
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
