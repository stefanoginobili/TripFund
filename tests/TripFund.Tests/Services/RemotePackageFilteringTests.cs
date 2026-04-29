using Moq;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using Xunit;
using System.IO.Compression;

namespace TripFund.Tests.Services;

public class RemotePackageFilteringTests : IDisposable
{
    private readonly string _tempPath;
    private readonly LocalStorageService _localStorage;
    private readonly RemoteStorageSyncEngine _syncEngine;
    private const string LocalDeviceId = "local-device-123";

    public RemotePackageFilteringTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);
        _localStorage = new LocalStorageService(_tempPath);
        _syncEngine = new RemoteStorageSyncEngine(_localStorage);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath)) Directory.Delete(_tempPath, true);
    }

    [Fact]
    public async Task SynchronizeAsync_ShouldFilterRemotePackagesCorrectly()
    {
        // Arrange
        var tripSlug = "filter-trip";
        var localTripPath = Path.Combine(_localStorage.TripsPath, tripSlug);
        Directory.CreateDirectory(localTripPath);

        // Set local device ID
        await _localStorage.SaveAppSettingsAsync(new AppSettings { DeviceId = LocalDeviceId });

        // Registry setup
        var registry = new LocalTripRegistry();
        registry.Trips[tripSlug] = new TripRegistryEntry
        {
            RemoteStorage = new RemoteStorageConfig
            {
                Provider = "onedrive",
                Parameters = new Dictionary<string, string> { { "folderId", "remote-root-id" } }
            }
        };
        await _localStorage.SaveTripRegistryAsync(registry);

        var mockFileSystem = new Mock<IRemoteFileSystem>();
        
        // 1. Root discovery
        mockFileSystem.Setup(f => f.ListChildrenAsync("remote-root-id", It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(new List<RemoteItem> { 
                new RemoteItem { Id = "devices_id", Name = "devices", IsFolder = true },
                new RemoteItem { Id = "packages_id", Name = "packages", IsFolder = true }
            });

        // 2. Devices discovery
        mockFileSystem.Setup(f => f.ListChildrenAsync("devices_id", It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(new List<RemoteItem> {
                new RemoteItem { Id = "device_root_id", Name = LocalDeviceId, IsFolder = true }
            });

        // 3. Packages discovery with various files
        var remotePackages = new List<RemoteItem>
        {
            new RemoteItem { Id = "pkg_ok", Name = "pack_20231001T120000Z_other-device.zip" },
            new RemoteItem { Id = "pkg_local", Name = $"pack_20231001T120000Z_{LocalDeviceId}.zip" },
            new RemoteItem { Id = "pkg_no_pack", Name = "random_20231001T120000Z_other.zip" },
            new RemoteItem { Id = "pkg_part", Name = "pack_20231001T120000Z_other-device.zip.part" },
            new RemoteItem { Id = "pkg_txt", Name = "pack_20231001T120000Z_other-device.txt" },
            new RemoteItem { Id = "pkg_wrong_ext", Name = "pack_20231001T120000Z_other-device.zip.bak" }
        };

        mockFileSystem.Setup(f => f.ListChildrenAsync("packages_id", It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(remotePackages);

        // Mock download for the expected package
        byte[] zipBytes;
        using (var ms = new MemoryStream())
        {
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                // Empty archive is fine for this test
            }
            zipBytes = ms.ToArray();
        }

        mockFileSystem.Setup(f => f.DownloadFileContentAsync("pkg_ok", It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(zipBytes);

        // Mock write check
        mockFileSystem.Setup(f => f.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(new RemoteItem { Id = "check_id" });

        // Act
        await _syncEngine.SynchronizeAsync(tripSlug, mockFileSystem.Object);

        // Assert
        // Verify only pkg_ok was downloaded
        mockFileSystem.Verify(f => f.DownloadFileContentAsync("pkg_ok", It.IsAny<Dictionary<string, string>>()), Times.Once);
        mockFileSystem.Verify(f => f.DownloadFileContentAsync("pkg_local", It.IsAny<Dictionary<string, string>>()), Times.Never);
        mockFileSystem.Verify(f => f.DownloadFileContentAsync("pkg_no_pack", It.IsAny<Dictionary<string, string>>()), Times.Never);
        mockFileSystem.Verify(f => f.DownloadFileContentAsync("pkg_part", It.IsAny<Dictionary<string, string>>()), Times.Never);
        mockFileSystem.Verify(f => f.DownloadFileContentAsync("pkg_txt", It.IsAny<Dictionary<string, string>>()), Times.Never);
        mockFileSystem.Verify(f => f.DownloadFileContentAsync("pkg_wrong_ext", It.IsAny<Dictionary<string, string>>()), Times.Never);

        // Verify it was added to appliedPackages
        var syncState = await _localStorage.GetLocalTripStorage(tripSlug).GetSyncStateAsync();
        syncState.Sync.Remote.AppliedPackages.Should().Contain("pack_20231001T120000Z_other-device.zip");
        syncState.Sync.Remote.AppliedPackages.Should().NotContain("pack_20231001T120000Z_other-device.zip.part");
    }
}
