using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Pages;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Components;

namespace TripFund.Tests.Pages;

public class TripManagementTests : BunitContext
{
    private readonly Mock<LocalTripStorageService> _storageMock;
    private readonly Mock<IRemoteStorageService> _remoteStorageMock;
    private readonly Mock<IAlertService> _alertMock;
    private readonly Mock<INativeDatePickerService> _datePickerMock;

    public TripManagementTests()
    {
        var itCulture = new System.Globalization.CultureInfo("it-IT");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = itCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = itCulture;

        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        _storageMock.Setup(s => s.TripsPath).Returns("dummy_trips_path");
        _remoteStorageMock = new Mock<IRemoteStorageService>();
        _alertMock = new Mock<IAlertService>();
        _datePickerMock = new Mock<INativeDatePickerService>();
        
        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_remoteStorageMock.Object);
        Services.AddSingleton(_alertMock.Object);
        Services.AddSingleton(_datePickerMock.Object);

        JSInterop.SetupVoid("appLogic.lockScroll");
        JSInterop.SetupVoid("appLogic.unlockScroll");
        JSInterop.SetupVoid("appLogic.positionMenu", _ => true);
    }

    [Fact]
    public async Task CreateTrip_ShouldSaveConfigAndRegistry_WithUniqueSlug()
    {
        // Arrange
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(new LocalTripRegistry());
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings { AuthorName = "Mario", DeviceId = "mario" });
        _remoteStorageMock.Setup(r => r.GetRemoteUniqueId("onedrive", It.IsAny<Dictionary<string, string>>())).Returns("123");

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/create-trip?provider=onedrive&folderId=123&folderName=TestFolder");

        var cut = Render<CreateTrip>();

        // Act
        // Find the Name input in GeneralInfoForm
        cut.Find("input[placeholder='es. Patagonia 2026']").Input("New Trip");
        
        // Add a currency (expanded by default now)
        cut.Find("input[placeholder='EUR']").Input("USD");
        cut.Find("input[placeholder='1000']").Input("500");
        await cut.Find(".confirm-btn").ClickAsync();
        
        // Slug generated from Name "New Trip" would be "new-trip"
        // Then unique ID "123" is appended: "new-trip_123"
        
        await cut.Find(".btn-primary-vibe").ClickAsync();

        // Assert
        _storageMock.Verify(s => s.SaveTripConfigAsync("new-trip_123", It.Is<TripConfig>(c => 
            c.Id == "new-trip_123" && 
            c.Name == "New Trip" && 
            c.Currencies.ContainsKey("USD")), "mario", It.IsAny<bool>()), Times.Once);

        _storageMock.Verify(s => s.SaveTripRegistryAsync(It.Is<LocalTripRegistry>(r => 
            r.Trips.ContainsKey("new-trip_123") && 
            r.Trips["new-trip_123"].RemoteStorage != null &&
            r.Trips["new-trip_123"].RemoteStorage!.Provider == "onedrive" &&
            r.Trips["new-trip_123"].RemoteStorage!.RemoteUniqueId == "123" &&
            r.Trips["new-trip_123"].RemoteStorage!.Readonly == false &&
            r.Trips["new-trip_123"].RemoteStorage!.Parameters["folderId"] == "123")), Times.Once);
    }

    [Fact]
    public void CreateTrip_ShouldShowCompleteSlugPreview_WithRemoteUniqueId()
    {
        // Arrange
        _remoteStorageMock.Setup(r => r.GetRemoteUniqueId("onedrive", It.IsAny<Dictionary<string, string>>())).Returns("abc12345");

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/create-trip?provider=onedrive&folderId=abc12345");

        var cut = Render<CreateTrip>();

        // Act
        var nameInput = cut.Find("input[placeholder='es. Patagonia 2026']");
        nameInput.Input("My New Trip");

        // Assert
        // The slug input is readonly and should contain the complete preview
        var slugInput = cut.FindAll("input").First(i => i.HasAttribute("readonly") && i.ParentElement!.ClassList.Contains("read-only"));
        slugInput.GetAttribute("value").Should().Be("my-new-trip_abc12345");
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

        await cut.Find(".btn-primary-vibe").ClickAsync();

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
        await cut.Find(".btn-primary-vibe").ClickAsync();

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
        cut.FindAll(".emoji-picker-container").Should().NotBeEmpty();

        // Click overlay to close
        await cut.Find(".popover-overlay").ClickAsync();

        // Assert picker is closed
        cut.FindAll(".emoji-picker-container").Should().BeEmpty();
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
        _alertMock.Setup(a => a.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AlertType>()))
            .ReturnsAsync(true);

        var cut = Render<EditTrip>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act
        // Open the header menu first
        await cut.Find(".header-actions button.icon-btn").ClickAsync();
        
        // Now click Delete
        await cut.Find(".dropdown-item-vibe.text-danger").ClickAsync();

        // Assert
        _alertMock.Verify(a => a.ConfirmAsync("Elimina Viaggio", It.IsAny<string>(), "Elimina", "Annulla", AlertType.Warning), Times.Once);
        _storageMock.Verify(s => s.DeleteTripAsync(tripSlug), Times.Once);
    }
}
