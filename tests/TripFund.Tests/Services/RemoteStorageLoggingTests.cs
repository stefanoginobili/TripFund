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
        
        var localStorage = new LocalStorageService(_tempPath);
        
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

        _authenticatorMock.Setup(a => a.AuthenticateAsync(It.IsAny<WebAuthenticatorOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebAuthenticatorResult());
    }

    [Fact]
    public void LogEntry_FollowsCorrectTimestampFormat()
    {
        // Arrange
        var logger = new RemoteStorageLogger();
        
        // Act
        logger.LogInfo("Testing format");
        var log = logger.GetLogContent();
        
        // Assert: [yyyy-MM-dd HH:mm:ss.fff Z]
        // Regex to match: [2023-10-27 10:00:00.000 Z]
        var pattern = @"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} Z\] INFO: Testing format";
        Assert.Matches(pattern, log);
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
        var localStorage = new LocalStorageService(_tempPath);
        await localStorage.SaveTripRegistryAsync(registry);
        
        var localTripDir = Path.Combine(localStorage.TripsPath, tripSlug);
        Directory.CreateDirectory(localTripDir);

        // EVALUATION PHASE mocks
        // 1. Mock root_id children to discover "devices" and "packages"
        _server.Given(Request.Create().WithPath("/me/drive/items/root_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [ { \"id\": \"devices_id\", \"name\": \"devices\", \"folder\": {} }, { \"id\": \"packages_id\", \"name\": \"packages\", \"folder\": {} } ] }"));

        // 2. Mock devices_id children to discover device folder
        _server.Given(Request.Create().WithPath("/me/drive/items/devices_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [ { \"id\": \"device_root_id\", \"name\": \"unknown-device\", \"folder\": {} } ] }"));

        // 3. Mock packages_id children (empty download phase)
        _server.Given(Request.Create().WithPath("/me/drive/items/packages_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [] }"));

        // 4. Mock permission check: Upload test file in device_root_id
        _server.Given(Request.Create().UsingPut().WithPath(new WildcardMatcher("/me/drive/items/device_root_id:/.last-seen:/content")))
            .RespondWith(Response.Create().WithStatusCode(201).WithBody("{ \"id\": \"test_file_id\" }"));

        // Act
        await _service.SynchronizeAsync(tripSlug);

        // Assert
        // 1. Verify log was saved LOCALLY in the timestamped path
        var logsDir = Path.Combine(localStorage.TripsPath, tripSlug, "sync", "logs");
        Assert.True(Directory.Exists(logsDir));
        var logFiles = Directory.GetFiles(logsDir, "*.log");
        Assert.Single(logFiles);
        
        var logFile = logFiles[0];
        var capturedLog = await File.ReadAllTextAsync(logFile);
        Assert.Contains("Starting synchronization for trip: local-log-test", capturedLog);
        Assert.Contains("API CALL: GET", capturedLog);
        Assert.Contains("Synchronization completed successfully.", capturedLog);

        // 2. Verify NO log upload happened
        var logUploadRequest = _server.LogEntries.FirstOrDefault(e => e.RequestMessage != null && e.RequestMessage.Method == "PUT" && (e.RequestMessage.Path.Contains($"{tripSlug}.txt") || e.RequestMessage.Path.Contains(".log")));
        Assert.Null(logUploadRequest);
        
        // Also verify no "debug" or "sync" folders were created on remote
        var createFolderRequest = _server.LogEntries.FirstOrDefault(e => e.RequestMessage != null && e.RequestMessage.Method == "POST" && (e.RequestMessage.Body?.Contains("debug") == true || e.RequestMessage.Body?.Contains("sync") == true));
        Assert.Null(createFolderRequest);
    }

    [Fact]
    public async Task SaveSyncLogLocallyAsync_ShouldRotateLogsKeepingOnlyLast20()
    {
        // Arrange
        var tripSlug = "rotation-test";
        var localStorage = new LocalStorageService(_tempPath);
        var engine = new RemoteStorageSyncEngine(localStorage);
        
        // Create 25 dummy log files in the sync/logs directory
        var logsDir = Path.Combine(_tempPath, "trips", tripSlug, "sync", "logs");
        Directory.CreateDirectory(logsDir);
        for (int i = 0; i < 25; i++)
        {
            // Use different timestamps to ensure they are distinct
            var oldTimestamp = DateTime.UtcNow.AddMinutes(-30 + i).ToString("yyyyMMddTHHmmssZ");
            File.WriteAllText(Path.Combine(logsDir, $"sync_{oldTimestamp}.log"), "Old log content");
        }

        var mockFileSystem = new Mock<IRemoteFileSystem>();
        var logger = new RemoteStorageLogger();
        logger.LogInfo("New log entry");
        mockFileSystem.Setup(f => f.Logger).Returns(logger);

        // Act: Invoke private method using reflection
        var method = typeof(RemoteStorageSyncEngine).GetMethod("SaveSyncLogLocallyAsync", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        await (Task)method!.Invoke(engine, new object[] { tripSlug, mockFileSystem.Object })!;

        // Assert
        var logFiles = Directory.GetFiles(logsDir, "sync_*.log");
        // We had 25, we added 1 during sync = 26. Rotation should keep 20.
        Assert.Equal(20, logFiles.Length);
        
        // Ensure the newest one exists
        var newestTimestamp = DateTime.UtcNow.ToString("yyyyMMddTHH"); // Check only up to hour to be safe with timing
        Assert.Contains(logFiles, f => Path.GetFileName(f).Contains(newestTimestamp));
    }

    public void Dispose()
    {
        _server.Stop();
        if (Directory.Exists(_tempPath)) Directory.Delete(_tempPath, true);
    }
}
