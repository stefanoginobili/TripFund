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

    public HomeTests()
    {
        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        Services.AddSingleton(_storageMock.Object);
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
    public void Home_WithTrips_ShouldRenderTripList()
    {
        // Arrange
        var registry = new LocalTripRegistry();
        registry.Trips.Add("trip-1", new TripRegistryEntry { DriveFolderId = "xyz" });
        
        var config = new TripConfig { Id = "guid-1", Name = "Patagonia", StartDate = DateTime.Now, EndDate = DateTime.Now.AddDays(10) };
        config.Members.Add("mario", new User { Name = "Mario" });

        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(registry);
        _storageMock.Setup(s => s.GetTripConfigAsync("trip-1")).ReturnsAsync(config);

        // Act
        var cut = Render<Home>();

        // Assert
        cut.Find(".trip-name").TextContent.Should().Be("Patagonia");
        cut.Find(".trip-members").TextContent.Should().Contain("1");
    }
}
