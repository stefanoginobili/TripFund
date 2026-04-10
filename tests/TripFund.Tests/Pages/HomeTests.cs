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
    private readonly Mock<ISyncService> _syncMock;
    private readonly Mock<IAlertService> _alertMock;

    public HomeTests()
    {
        var itCulture = new System.Globalization.CultureInfo("it-IT");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = itCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = itCulture;

        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        _syncMock = new Mock<ISyncService>();
        _alertMock = new Mock<IAlertService>();
        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_syncMock.Object);
        Services.AddSingleton(_alertMock.Object);
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
        registry.Trips.Add("current", new TripRegistryEntry { RemoteStorage = new RemoteStorageConfig { Provider = "google-drive", Parameters = new() { { "folderId", "1" } } } });
        registry.Trips.Add("future", new TripRegistryEntry { RemoteStorage = new RemoteStorageConfig { Provider = "google-drive", Parameters = new() { { "folderId", "2" } } } });
        registry.Trips.Add("past", new TripRegistryEntry { RemoteStorage = new RemoteStorageConfig { Provider = "google-drive", Parameters = new() { { "folderId", "3" } } } });

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
}
