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
        var itCulture = new System.Globalization.CultureInfo("it-IT");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = itCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = itCulture;

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
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings { AuthorName = "Mario", DeviceId = "mario" });
        _driveMock.Setup(d => d.PickFolderAsync()).ReturnsAsync(new DriveFolder { Id = "drive-123", Name = "Drive Folder" });

        var cut = Render<CreateTrip>();

        // Act
        // Find the Name input in GeneralInfoForm
        cut.Find("input[placeholder='es. Patagonia 2026']").Input("New Trip");
        
        // Slug should be generated automatically: "new-trip"
        
        await cut.Find(".save-btn-large").ClickAsync();

        // Assert
        _storageMock.Verify(s => s.SaveTripConfigAsync("new-trip", It.Is<TripConfig>(c => c.Name == "New Trip"), "mario", It.IsAny<bool>()), Times.Once);
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
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings { AuthorName = "Mario", DeviceId = "mario" });

        var cut = Render<EditTrip>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act
        var nameInput = cut.Find("input[placeholder='es. Patagonia 2026']");
        nameInput.Input("Updated Name");

        await cut.Find(".save-btn-large").ClickAsync();

        // Assert
        _storageMock.Verify(s => s.SaveTripConfigAsync(tripSlug, It.Is<TripConfig>(c => c.Name == "Updated Name"), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public async Task EditTrip_AddMemberWithEmoji_ShouldUpdateConfig()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig 
        { 
            Id = "guid-1", 
            Name = "Test Trip", 
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario", Avatar = "👨" } } },
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€", ExpectedQuotaPerMember = 100 } } }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings { AuthorName = "Mario", DeviceId = "mario" });

        var cut = Render<EditTrip>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act
        // Expand the "Aggiungi Partecipante" section
        await cut.Find(".add-member-dashed").ClickAsync();

        // Open emoji picker
        await cut.Find(".avatar-input-box").ClickAsync();
        
        // Select an emoji (e.g., the second one)
        var emojiButtons = cut.FindAll(".emoji-btn");
        await emojiButtons[1].ClickAsync();

        // Fill name
        cut.Find("input[placeholder='Esempio: Andrea']").Input("Luigi");
        
        // Click add (confirm-btn inside the expanded form)
        await cut.Find(".confirm-btn").ClickAsync();

        // Save
        await cut.Find(".save-btn-large").ClickAsync();

        // Assert
        _storageMock.Verify(s => s.SaveTripConfigAsync(tripSlug, It.Is<TripConfig>(c => 
            c.Members.ContainsKey("luigi") && 
            c.Members["luigi"].Name == "Luigi" &&
            c.Members["luigi"].Avatar != "👤"), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public async Task EditTrip_ClickOutsideEmojiPicker_ShouldClosePicker()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig 
        { 
            Id = "guid-1", 
            Name = "Test Trip", 
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario", Avatar = "👨" } } },
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€", ExpectedQuotaPerMember = 100 } } }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings { AuthorName = "Mario", DeviceId = "mario" });

        var cut = Render<EditTrip>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act
        // Expand the "Aggiungi Partecipante" section
        await cut.Find(".add-member-dashed").ClickAsync();

        // Open emoji picker
        await cut.Find(".avatar-input-box").ClickAsync();
        
        // Assert picker is open
        cut.FindAll(".emoji-picker-popover").Should().NotBeEmpty();

        // Click overlay to close
        await cut.Find(".popover-overlay").ClickAsync();

        // Assert picker is closed
        cut.FindAll(".emoji-picker-popover").Should().BeEmpty();
    }

    [Fact]
    public async Task EditTrip_ClickAddMember_WhileEditingAnother_ShouldClosePreviousEditor()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig 
        { 
            Id = "guid-1", 
            Name = "Test Trip", 
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario", Avatar = "👨" } } },
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€", ExpectedQuotaPerMember = 100 } } }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings { AuthorName = "Mario", DeviceId = "mario" });

        var cut = Render<EditTrip>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act 1: Start editing Mario directly
        var startEditMethod = cut.Instance.GetType().GetMethod("StartEditMember", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        startEditMethod.Should().NotBeNull();
        await cut.InvokeAsync(async () => 
        {
            var task = startEditMethod!.Invoke(cut.Instance, new object[] { "mario", config.Members["mario"] }) as Task;
            if (task != null) await task;
        });
        
        // Assert: Mario edit form is open
        cut.Find(".new-member-title").TextContent.Should().Contain("Modifica Partecipante");
        
        // Act 2: Click "Aggiungi Partecipante"
        await cut.Find(".add-member-dashed").ClickAsync();

        // Assert: Mario edit form is closed, New Member form is open
        cut.FindAll(".member-edit-container").Should().BeEmpty();
        cut.FindAll(".new-member-form").Should().NotBeEmpty();
    }

    [Fact]
    public async Task EditTrip_Delete_ShouldConfirmAndCallStorage()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig { Id = "1", Name = "Test" };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings { AuthorName = "M", DeviceId = "m" });
        _alertMock.Setup(a => a.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var cut = Render<EditTrip>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act
        // Open the header menu first
        await cut.Find(".header-actions button.icon-btn").ClickAsync();
        
        // Now click Delete
        await cut.Find(".dropdown-item-custom.text-danger").ClickAsync();

        // Assert
        _alertMock.Verify(a => a.ConfirmAsync("Elimina Viaggio", It.IsAny<string>(), "Elimina", "Annulla"), Times.Once);
        _storageMock.Verify(s => s.DeleteTripAsync(tripSlug), Times.Once);
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
        _storageMock.Verify(s => s.SaveTripConfigAsync("existing-trip", It.IsAny<TripConfig>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        _storageMock.Verify(s => s.SaveTripRegistryAsync(It.Is<LocalTripRegistry>(r => r.Trips.ContainsKey("existing-trip"))), Times.Once);
    }
}
