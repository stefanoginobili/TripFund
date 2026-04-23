using IWebAuthenticator = TripFund.App.Services.IWebAuthenticator;
using System.Net;
using System.IO.Compression;
using Moq;
using TripFund.App.Models;
using TripFund.App.Services;
using AppConstants = TripFund.App.Constants.AppConstants;
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
    private readonly LocalTripStorageService _localStorage;
    private readonly string _tempPath;

    public OneDriveSyncLogicTests()
    {
        _server = WireMockServer.Start();
        _authenticatorMock = new Mock<IWebAuthenticator>();
        _configMock = new Mock<IMicrosoftAuthConfiguration>();
        
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);
        
        _localStorage = new LocalTripStorageService(_tempPath);
        
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var client = new HttpClient { BaseAddress = new Uri(_server.Urls[0]) };
        httpClientFactoryMock.Setup(f => f.CreateClient(nameof(OneDriveRemoteStorageService))).Returns(client);

        var syncEngine = new RemoteStorageSyncEngine(_localStorage);

        _service = new OneDriveRemoteStorageService(
            httpClientFactoryMock.Object,
            _authenticatorMock.Object,
            _localStorage,
            _configMock.Object,
            syncEngine,
            _server.Urls[0]);

        // Mock Authentication
        _authenticatorMock.Setup(a => a.AuthenticateAsync(It.IsAny<WebAuthenticatorOptions>()))
            .ReturnsAsync(new WebAuthenticatorResult());
    }

    [Fact]
    public async Task GetRemoteTripMetadataAsync_CorrectlyLoadsMetadata_WhenTripFundFileExists()
    {
        var parameters = new Dictionary<string, string> { { "folderId", "root_id" }, { "accessToken", "fake_token" }, { "accessTokenExpiry", DateTime.Now.AddHours(1).ToString("O") } };
        
        // 1. Mock GetChildItemAsync(root_id, ".tripfund")
        _server.Given(Request.Create().WithPath("/me/drive/items/root_id:/.tripfund").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"id\": \"tripfund_id\", \"name\": \".tripfund\", \"file\": {} }"));

        // 2. Mock DownloadFileContentAsync(tripfund_id)
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("contentType=tripfund/trip");
        sb.AppendLine($"{AppConstants.Metadata.TripSlug}=test-trip");
        sb.AppendLine("author=Mario Rossi");
        sb.AppendLine("createdAt=2024-01-01T12:00:00.000Z");
        
        _server.Given(Request.Create().WithPath("/me/drive/items/tripfund_id/content").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(sb.ToString()));

        // Act
        var result = await _service.GetRemoteTripMetadataAsync("onedrive", parameters);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Equal("test-trip", result.TripSlug);
        Assert.Equal("Mario Rossi", result.Author);
    }

    private bool _testFailed = false;

    [Fact]
    public async Task SynchronizeAsync_EvaluatesDownloadAndUploadCorrectly()
    {
        try 
        {
            // Arrange
            var tripSlug = "test-trip";
            var localTripPath = Path.Combine(_tempPath, "trips", tripSlug);
            Directory.CreateDirectory(localTripPath);
            
            var deviceId = "local-device-id";
            await _localStorage.SaveAppSettingsAsync(new AppSettings { DeviceId = deviceId });

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
            await _localStorage.SaveTripRegistryAsync(registry);

            // 1. EVALUATION PHASE mocks
            // Initialize metadata (.tripfund)
            _server.Given(Request.Create().WithPath("/me/drive/items/root_id:/.tripfund:/content").UsingPut())
                .RespondWith(Response.Create().WithStatusCode(201).WithBody("{ \"id\": \"tripfund_id\" }"));

            // Discover "devices" and "packages" folders via ListChildrenAsync(root_id)
            _server.Given(Request.Create().WithPath("/me/drive/items/root_id/children").UsingGet())
                .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [ { \"id\": \"devices_id\", \"name\": \"devices\", \"folder\": {} }, { \"id\": \"packages_id\", \"name\": \"packages\", \"folder\": {} } ] }"));

            // Get or Create device folder via discovery in ListChildrenAsync(devices_id)
            _server.Given(Request.Create().WithPath("/me/drive/items/devices_id/children").UsingGet())
                .RespondWith(Response.Create().WithStatusCode(200).WithBody($"{{ \"value\": [ {{ \"id\": \"device_root_id\", \"name\": \"{deviceId}\", \"folder\": {{}} }} ] }}"));

            // Check write permission (.last-seen)
            _server.Given(Request.Create().WithPath("/me/drive/items/device_root_id:/.last-seen:/content").UsingPut())
                .RespondWith(Response.Create().WithStatusCode(201).WithBody("{ \"id\": \"check_id\" }"));

            // 2. DOWNLOAD PHASE mocks
            var remotePkgName = "pack_20240101T000000Z_remote-dev.zip";
            _server.Given(Request.Create().WithPath("/me/drive/items/packages_id/children").UsingGet())
                .RespondWith(Response.Create().WithStatusCode(200).WithBody($"{{ \"value\": [ {{ \"id\": \"pkg_id\", \"name\": \"{remotePkgName}\", \"file\": {{}} }} ] }}"));

            // Create a fake ZIP content for download
            byte[] zipBytes;
            using (var ms = new MemoryStream())
            {
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    var entry = archive.CreateEntry("config_versioned/002_UPD_remote-dev/.tripfund");
                    using (var writer = new StreamWriter(entry.Open())) writer.Write("author=remote-user\nversioning.parents=001_NEW_local-device-id");
                    
                    var dataEntry = archive.CreateEntry("config_versioned/002_UPD_remote-dev/.content/trip_config.json");
                    using (var writer = new StreamWriter(dataEntry.Open())) writer.Write("{ \"Name\": \"Remote Trip\" }");
                }
                zipBytes = ms.ToArray();
            }

            _server.Given(Request.Create().WithPath("/me/drive/items/pkg_id/content").UsingGet())
                .RespondWith(Response.Create().WithStatusCode(200).WithBody(zipBytes));

            // 3. UPLOAD PHASE mocks
            // Prepare a local pending upload
            await _localStorage.SaveTripConfigAsync(tripSlug, new TripConfig { Name = "Local Trip" }, deviceId);
            // This creates config_versioned/001_NEW_local-device-id/

            // Expect upload of .zip directly
            _server.Given(Request.Create().WithPath($"/me/drive/items/packages_id:/pack_*.zip:/content").UsingPut())
                .RespondWith(Response.Create().WithStatusCode(201).WithBody("{ \"id\": \"uploaded_pkg_id\", \"name\": \"pack_...zip\" }"));
            
            // Act
            await _service.InitializeRemoteLocationAsync(tripSlug, "onedrive", registry.Trips[tripSlug]!.RemoteStorage!.Parameters);
            await _service.SynchronizeAsync(tripSlug);

            // Assert
            // Verify Download worked
            var remoteLeaf = Path.Combine(localTripPath, "config_versioned", "002_UPD_remote-dev");
            Assert.True(File.Exists(Path.Combine(remoteLeaf, ".tripfund")));
            Assert.True(File.Exists(Path.Combine(remoteLeaf, ".content", "trip_config.json")));

            // Verify SyncState updated
            var syncState = await _localStorage.GetSyncStateAsync(tripSlug);
            Assert.Contains(remotePkgName, syncState.Sync.Remote.AppliedPackages);
            Assert.Empty(syncState.Sync.Local.Pending); // Should be cleared after success
            // Verify Upload was called
            var uploadRequests = _server.FindLogEntries(Request.Create().UsingPut().WithPath(new WildcardMatcher("*/packages_id:/*.zip:/content")));
            Assert.Single(uploadRequests);
        }
        catch (Exception)
        {
            _testFailed = true;
            throw;
        }
    }

    [Fact]
    public async Task SynchronizeAsync_UsesUploadSession_WhenPackageIsLarge()
    {
        // Arrange
        var tripSlug = "large-trip";
        var localTripPath = Path.Combine(_tempPath, "trips", tripSlug);
        Directory.CreateDirectory(localTripPath);
        var deviceId = "large-device";
        await _localStorage.SaveAppSettingsAsync(new AppSettings { DeviceId = deviceId });

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
        await _localStorage.SaveTripRegistryAsync(registry);

        // Mocks for Phase 1 & 2 (Evaluation & Download - empty)
        _server.Given(Request.Create().WithPath("/me/drive/items/root_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [ { \"id\": \"devices_id\", \"name\": \"devices\", \"folder\": {} }, { \"id\": \"packages_id\", \"name\": \"packages\", \"folder\": {} } ] }"));
        _server.Given(Request.Create().WithPath("/me/drive/items/devices_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [] }"));
        _server.Given(Request.Create().WithPath("/me/drive/items/devices_id/children").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithBody("{ \"id\": \"device_root_id\", \"name\": \"large-device\" }"));
        _server.Given(Request.Create().WithPath("/me/drive/items/device_root_id:/.last-seen:/content").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(201).WithBody("{ \"id\": \"check_id\" }"));
        _server.Given(Request.Create().WithPath("/me/drive/items/packages_id/children").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{ \"value\": [] }"));

        // Phase 3: Prepare LARGE content (> 2MB)
        await _localStorage.SaveTripConfigAsync(tripSlug, new TripConfig { Name = "Large Trip" }, deviceId);
        var pendingLeaf = Path.Combine(localTripPath, "config_versioned", $"001_NEW_{deviceId}");
        var largeFile = Path.Combine(pendingLeaf, ".content", "large.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(largeFile)!);
        byte[] largeData = new byte[3 * 1024 * 1024]; // 3 MB
        new Random().NextBytes(largeData);
        await File.WriteAllBytesAsync(largeFile, largeData);

        // Mock Upload Session
        var uploadUrl = $"{_server.Urls[0]}/upload/session/123";
        _server.Given(Request.Create().WithPath(new WildcardMatcher("*/packages_id:/*.zip:/createUploadSession")).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody($"{{ \"uploadUrl\": \"{uploadUrl}\" }}"));

        // Mock Chunk Uploads
        _server.Given(Request.Create().WithPath("/upload/session/123").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(201).WithBody("{ \"id\": \"uploaded_pkg_id\", \"name\": \"large.zip\" }"));

        // Act
        await _service.SynchronizeAsync(tripSlug);

        // Assert
        var sessionRequests = _server.FindLogEntries(Request.Create().UsingPost().WithPath(new WildcardMatcher("*/createUploadSession")));
        Assert.Single(sessionRequests);

        var chunkRequests = _server.FindLogEntries(Request.Create().UsingPut().WithPath("/upload/session/123"));
        Assert.NotEmpty(chunkRequests);
    }

    public void Dispose()
    {
        _server.Stop();
        if (!_testFailed && Directory.Exists(_tempPath)) Directory.Delete(_tempPath, true);
    }
}
