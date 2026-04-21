using Moq;
using TripFund.App.Models;
using TripFund.App.Services;
using Xunit;

namespace TripFund.Tests.Services;

public class ScreenLockTests
{
    private readonly Mock<OneDriveRemoteStorageService> _onedriveMock;
    private readonly Mock<LocalTripStorageService> _storageMock;
    private readonly CompositeRemoteStorageService _service;

    public ScreenLockTests()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "TripFundTests_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        _storageMock = new Mock<LocalTripStorageService>(tempPath);
        _onedriveMock = new Mock<OneDriveRemoteStorageService>(
            new Mock<IHttpClientFactory>().Object,
            new Mock<TripFund.App.Services.IWebAuthenticator>().Object,
            _storageMock.Object,
            new Mock<IMicrosoftAuthConfiguration>().Object,
            new Mock<RemoteStorageSyncEngine>(_storageMock.Object).Object,
            "https://graph.microsoft.com/v1.0"
        );

        _service = new CompositeRemoteStorageService(_onedriveMock.Object, _storageMock.Object);
    }


    [Fact]
    public async Task SynchronizeAsync_DoesNotCrashWhenMauiApisAreMissing()
    {
        // Arrange
        var tripSlug = "test-trip";
        var registry = new LocalTripRegistry();
        registry.Trips[tripSlug] = new TripRegistryEntry
        {
            RemoteStorage = new RemoteStorageConfig
            {
                Provider = "onedrive",
                Parameters = new Dictionary<string, string> { { "folderId", "123" } }
            }
        };

        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(registry);
        _storageMock.Setup(s => s.GetSyncStateAsync(tripSlug)).ReturnsAsync(new SyncState());
        _onedriveMock.Setup(s => s.SynchronizeAsync(tripSlug)).Returns(Task.CompletedTask);

        // Act
        // This should NOT throw even if DeviceDisplay/MainThread are not initialized in the test environment
        var exception = await Record.ExceptionAsync(() => _service.SynchronizeAsync(tripSlug));

        // Assert
        Assert.Null(exception);
        _onedriveMock.Verify(s => s.SynchronizeAsync(tripSlug), Times.Once);
    }
}
