using IWebAuthenticator = TripFund.App.Services.IWebAuthenticator;
using Moq;
using TripFund.App.Models;
using TripFund.App.Services;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace TripFund.Tests.Services;

public class RemoteStorageLoggingTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly Mock<IWebAuthenticator> _authenticatorMock;
    private readonly Mock<IMicrosoftAuthConfiguration> _configMock;
    private readonly OneDriveRemoteStorageService _service;
    private readonly string _tempPath;

    public RemoteStorageLoggingTests()
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

        _authenticatorMock.Setup(a => a.AuthenticateAsync(It.IsAny<WebAuthenticatorOptions>()))
            .ReturnsAsync(new WebAuthenticatorResult());
    }

    [Fact]
    public async Task LogApiCall_IncludesFolderName_WhenSet()
    {
        // Arrange
        var logger = new RemoteStorageLogger();
        logger.CurrentFolderName = "TestFolder";
        
        // Act
        logger.LogApiCall("GET", "https://api.example.com", "Testing call");
        var log = logger.GetLogContent();
        
        // Assert
        Assert.Contains("[TestFolder] Testing call", log);
    }

    [Fact]
    public async Task OneDriveService_IncludesFolderNameInDescription_UsingCache()
    {
        // Arrange
        var logger = new RemoteStorageLogger();
        _service.Logger = logger;
        
        var parameters = new Dictionary<string, string>
        {
            ["accessToken"] = "fake-token",
            ["accessTokenExpiry"] = DateTime.Now.AddHours(1).ToString("O")
        };

        // Populate the cache by listing children (simulated response contains IDs and names)
        _server.Given(Request.Create().WithPath("/me/drive/items/root/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{\"value\":[{\"id\":\"folder-123\",\"name\":\"MyTransactions\",\"folder\":{}}]}"));

        var fs = (IRemoteFileSystem)_service;
        await fs.ListChildrenAsync("root", parameters);
        logger.Clear();

        // Act: Listing children for the newly cached folder
        _server.Given(Request.Create().WithPath("/me/drive/items/folder-123/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{\"value\":[]}"));

        await fs.ListChildrenAsync("folder-123", parameters);
        var log = logger.GetLogContent();

        // Assert
        Assert.Contains("Listing children for folder 'MyTransactions'", log);
    }

    [Fact]
    public async Task SynchronizeAsync_SavesLogLocallyAndNeverUploadsIt()
    {
        // Arrange
        var tripSlug = "local-log-test";
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
        
        var localTripDir = Path.Combine(localStorage.TripsPath, tripSlug);
        Directory.CreateDirectory(localTripDir);

        // Mock permission check: List empty
        _server.Given(Request.Create().WithPath("/me/drive/items/root_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [] }"));
        // Mock permission check: Upload test file
        _server.Given(Request.Create().UsingPut().WithPath(new WildcardMatcher("/me/drive/items/root_id:/.rw-test-*:/content")))
            .RespondWith(Response.Create().WithStatusCode(201).WithBody("{ \"id\": \"test_file_id\" }"));
        // Mock permission check: Delete test file
        _server.Given(Request.Create().UsingDelete().WithPath("/me/drive/items/test_file_id"))
            .RespondWith(Response.Create().WithStatusCode(204));

        // Act
        await _service.SynchronizeAsync(tripSlug);

        // Assert
        // 1. Verify log was saved LOCALLY in sibling debug folder
        var debugSyncDir = Path.Combine(localStorage.AppDataPath, "debug", "sync");
        var logFile = Path.Combine(debugSyncDir, $"{tripSlug}.txt");
        Assert.True(File.Exists(logFile));
        
        var capturedLog = await File.ReadAllTextAsync(logFile);
        Assert.Contains("Starting synchronization for trip: local-log-test", capturedLog);
        Assert.Contains("API CALL: GET", capturedLog);
        Assert.Contains("Synchronization completed successfully.", capturedLog);

        // 2. Verify NO log upload happened
        var logUploadRequest = _server.LogEntries.FirstOrDefault(e => e.RequestMessage != null && e.RequestMessage.Method == "PUT" && e.RequestMessage.Path.Contains($"{tripSlug}.txt"));
        Assert.Null(logUploadRequest);
        
        // Also verify no "debug" or "sync" folders were created on remote
        var createFolderRequest = _server.LogEntries.FirstOrDefault(e => e.RequestMessage != null && e.RequestMessage.Method == "POST" && (e.RequestMessage.Body?.Contains("debug") == true || e.RequestMessage.Body?.Contains("sync") == true));
        Assert.Null(createFolderRequest);
    }

    public void Dispose()
    {
        _server.Stop();
        if (Directory.Exists(_tempPath)) Directory.Delete(_tempPath, true);
    }
}
