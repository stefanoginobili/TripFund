using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Pages;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;

namespace TripFund.Tests.Pages;

public class TripManagementTests : BunitContext
{
    private readonly Mock<LocalTripStorageService> _storageMock;
    private readonly Mock<IDriveService> _driveMock;
    private readonly Mock<IAlertService> _alertMock;

    public TripManagementTests()
    {
        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        _driveMock = new Mock<IDriveService>();
        _alertMock = new Mock<IAlertService>();
        
        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_driveMock.Object);
        Services.AddSingleton(_alertMock.Object);
    }

    [Fact]
    public async Task CreateTrip_ShouldSaveConfigAndRegistry()
    {
        // Arrange
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(new LocalTripRegistry());
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings { AuthorName = "Mario", AuthorSlug = "mario" });
        _driveMock.Setup(d => d.PickFolderAsync()).ReturnsAsync(new DriveFolder { Id = "drive-123", Name = "Drive Folder" });

        var cut = Render<CreateTrip>();

        // Act
        cut.Find("input[type='text']").Input("New Trip"); // This triggers OnNameInput (slug generation)
        
        // Re-find inputs because the first one might have caused a re-render
        var inputs = cut.FindAll("input[type='text']");
        inputs[1].Change("new-trip"); // Manually setting slug to be sure

        // Add a currency is already there by default (EUR)
        
        await cut.Find(".btn-submit").ClickAsync();

        // Assert
        _storageMock.Verify(s => s.SaveTripConfigAsync("new-trip", It.Is<TripConfig>(c => c.Name == "New Trip")), Times.Once);
        _storageMock.Verify(s => s.SaveTripRegistryAsync(It.Is<LocalTripRegistry>(r => r.Trips.ContainsKey("new-trip"))), Times.Once);
    }

    [Fact]
    public async Task EditTrip_ShouldUpdateConfig()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig 
        { 
            Id = "guid-1", 
            Name = "Old Name", 
            StartDate = DateTime.Now, 
            EndDate = DateTime.Now.AddDays(1),
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€", ExpectedQuotaPerMember = 100 } } }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings { AuthorName = "Mario", AuthorSlug = "mario" });

        var cut = Render<EditTrip>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act
        var nameInput = cut.FindAll("input").First(i => i.GetAttribute("placeholder") == "es. Patagonia 2026");
        nameInput.Change("Updated Name");

        await cut.Find(".save-btn-large").ClickAsync();

        // Assert
        _storageMock.Verify(s => s.SaveTripConfigAsync(tripSlug, It.Is<TripConfig>(c => c.Name == "Updated Name")), Times.Once);
    }

    [Fact]
    public async Task JoinTrip_Home_ShouldRegisterAndMockConfig()
    {
        // Arrange
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(new LocalTripRegistry());
        _storageMock.Setup(s => s.GetTripConfigAsync(It.IsAny<string>())).ReturnsAsync((TripConfig?)null);
        _driveMock.Setup(d => d.PickFolderAsync()).ReturnsAsync(new DriveFolder { Id = "drive-456", Name = "Existing Trip" });

        var cut = Render<Home>();

        // Act
        await cut.Find("button.btn-add").ClickAsync();

        // Assert
        _storageMock.Verify(s => s.SaveTripConfigAsync("existing-trip", It.IsAny<TripConfig>()), Times.Once);
        _storageMock.Verify(s => s.SaveTripRegistryAsync(It.Is<LocalTripRegistry>(r => r.Trips.ContainsKey("existing-trip"))), Times.Once);
    }
}
