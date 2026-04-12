using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Pages;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;

namespace TripFund.Tests.Pages;

public class HomeTests : BunitContext
{
    private readonly Mock<LocalTripStorageService> _storageMock;
    private readonly Mock<IRemoteStorageService> _remoteStorageMock;
    private readonly Mock<IAlertService> _alertMock;
    private readonly Mock<OneDriveRemoteStorageService> _oneDriveMock;
    private readonly Mock<IMicrosoftAuthConfiguration> _msAuthConfigMock;
    private readonly Mock<IOneDrivePickerService> _oneDrivePickerMock;

    public HomeTests()
    {
        var itCulture = new System.Globalization.CultureInfo("it-IT");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = itCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = itCulture;

        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        _remoteStorageMock = new Mock<IRemoteStorageService>();
        _alertMock = new Mock<IAlertService>();
        _msAuthConfigMock = new Mock<IMicrosoftAuthConfiguration>();
        _oneDrivePickerMock = new Mock<IOneDrivePickerService>();
        
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _oneDriveMock = new Mock<OneDriveRemoteStorageService>(
            httpClientFactoryMock.Object, 
            new Mock<TripFund.App.Services.IWebAuthenticator>().Object, 
            _storageMock.Object, 
            _msAuthConfigMock.Object,
            new RemoteStorageSyncEngine(_storageMock.Object),
            "https://graph.microsoft.com/v1.0");

        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_remoteStorageMock.Object);
        Services.AddSingleton(_alertMock.Object);
        Services.AddSingleton(_msAuthConfigMock.Object);
        Services.AddSingleton(_oneDriveMock.Object);
        Services.AddSingleton(_oneDrivePickerMock.Object);
        // Also register OneDrivePickerService as itself if needed, but the component uses OneDrivePickerService concrete class?
        // Let's check OneDrivePickerModal.razor
    }

    [Fact]
    public void Home_NoTrips_ShouldRenderEmptyState()
    {
        // Arrange
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(new LocalTripRegistry());

        // Act
        var cut = Render<Home>();

        // Assert
        cut.Find(".empty-state").Should().NotBeNull();
        cut.Find(".empty-state p").TextContent.Should().Be("Nessun viaggio trovato.");
    }

    [Fact]
    public void Home_WithTrips_ShouldCategorizeCorrectly()
    {
        // Arrange
        var registry = new LocalTripRegistry();
        registry.Trips.Add("current", new TripRegistryEntry { RemoteStorage = new RemoteStorageConfig { Provider = "onedrive", Parameters = new() { { "folderId", "1" } } } });
        registry.Trips.Add("future", new TripRegistryEntry { RemoteStorage = new RemoteStorageConfig { Provider = "onedrive", Parameters = new() { { "folderId", "2" } } } });
        registry.Trips.Add("past", new TripRegistryEntry { RemoteStorage = new RemoteStorageConfig { Provider = "onedrive", Parameters = new() { { "folderId", "3" } } } });

        var today = DateTime.Today;
        
        var current = new TripConfig { Id = "c", Name = "Current Trip", StartDate = today, EndDate = today.AddDays(5) };
        current.Members.Add("m1", new User { Name = "M1" });

        var future = new TripConfig { Id = "f", Name = "Future Trip", StartDate = today.AddDays(10), EndDate = today.AddDays(15) };
        future.Members.Add("m1", new User { Name = "M1" });
        future.Members.Add("m2", new User { Name = "M2" });

        var past = new TripConfig { Id = "p", Name = "Past Trip", StartDate = today.AddDays(-20), EndDate = today.AddDays(-10) };

        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(registry);
        _storageMock.Setup(s => s.GetTripConfigAsync("current")).ReturnsAsync(current);
        _storageMock.Setup(s => s.GetTripConfigAsync("future")).ReturnsAsync(future);
        _storageMock.Setup(s => s.GetTripConfigAsync("past")).ReturnsAsync(past);

        // Act
        var cut = Render<Home>();

        // Assert
        // Sections
        cut.FindAll(".section-header-vibe").Select(e => e.TextContent).Should().Contain(new[] { "IN CORSO", "FUTURI", "PASSATI" });

        // Icons
        cut.FindAll(".trip-icon").Select(e => e.TextContent).Should().Contain(new[] { "🛫", "🕒", "🏁" });

        // Grayscale for past
        cut.Find(".trip-card.grayscale").InnerHtml.Should().Contain("Past Trip");

        // Pluralization
        var badges = cut.FindAll(".member-badge").Select(e => e.TextContent).ToList();
        badges.Any(b => b.Contains("1 Partecipante")).Should().BeTrue();
        badges.Any(b => b.Contains("2 Partecipanti")).Should().BeTrue();
        badges.Any(b => b.Contains("0 Partecipanti")).Should().BeTrue();
    }

    [Fact]
    public async Task HandleJoinTrip_ShouldUseUniqueSlugAndFailIfFolderExists()
    {
        // Arrange
        var registry = new LocalTripRegistry();
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(registry);
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings { DeviceId = "dev-1" });
        _storageMock.Setup(s => s.TripsPath).Returns(Path.Combine(Path.GetTempPath(), "TripFundTests_Trips"));
        
        var remoteConfig = new TripConfig { Name = "Existing Trip", StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(1) };
        _remoteStorageMock.Setup(r => r.GetRemoteTripConfigAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(remoteConfig);
        _remoteStorageMock.Setup(r => r.GetRemoteUniqueId(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .Returns("remote-123");
        
        _alertMock.Setup(a => a.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), "Conferma", "Annulla"))
            .ReturnsAsync(true);

        var cut = Render<Home>();

        var selection = new RemoteStorageSelection 
        { 
            Provider = "onedrive", 
            Parameters = new Dictionary<string, string> { { "folderId", "remote-123" } } 
        };

        // Act & Assert 1: Normal join
        var handleJoinMethod = cut.Instance.GetType().GetMethod("HandleJoinTrip", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await cut.InvokeAsync(async () => 
        {
            var task = handleJoinMethod!.Invoke(cut.Instance, new object[] { selection }) as Task;
            if (task != null) await task;
        });

        // Expected slug: existing-trip_remote-123
        var expectedSlug = "existing-trip_remote-123";
        _storageMock.Verify(s => s.SaveTripConfigAsync(expectedSlug, It.IsAny<TripConfig>(), "dev-1", It.IsAny<bool>()), Times.Once);
        _storageMock.Verify(s => s.SaveTripRegistryAsync(It.Is<LocalTripRegistry>(r => 
            r.Trips.ContainsKey(expectedSlug) &&
            r.Trips[expectedSlug].RemoteStorage != null &&
            r.Trips[expectedSlug].RemoteStorage!.RemoteUniqueId == "remote-123" &&
            r.Trips[expectedSlug].RemoteStorage!.Readonly == true)), Times.Once);

        // Act & Assert 2: Join again, should fail because directory exists
        // We simulate Directory.Exists returning true by setting up a real directory or mocking if possible.
        // Since we can't easily mock static Directory.Exists, we'll ensure the TripsPath exists and create the folder.
        var tripsPath = _storageMock.Object.TripsPath;
        if (!Directory.Exists(tripsPath)) Directory.CreateDirectory(tripsPath);
        var tripDir = Path.Combine(tripsPath, expectedSlug);
        Directory.CreateDirectory(tripDir);

        await cut.InvokeAsync(async () => 
        {
            var task = handleJoinMethod!.Invoke(cut.Instance, new object[] { selection }) as Task;
            if (task != null) await task;
        });

        _alertMock.Verify(a => a.ShowAlertAsync("Errore", "Questo viaggio è già stato importato localmente."), Times.Once);
        
        // Cleanup
        if (Directory.Exists(tripsPath)) Directory.Delete(tripsPath, true);
    }
}
