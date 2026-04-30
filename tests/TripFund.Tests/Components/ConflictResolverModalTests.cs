using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Common;
using TripFund.App.Services;
using FluentAssertions;
using System.Collections.Generic;
using System.Threading.Tasks;
using TripFund.App.Models;
using System;
using System.Linq;

namespace TripFund.Tests.Components;

public class ConflictResolverModalTests : BunitContext
{
    private readonly Mock<LocalStorageService> _storageMock;
    private readonly Mock<LocalTripStorage> _tripStorageMock;
    private readonly Mock<IAlertService> _alertServiceMock;

    public ConflictResolverModalTests()
    {
        // Mock with a temporary path to avoid FileSystem.AppDataDirectory
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);
        
        _storageMock = new Mock<LocalStorageService>(tempPath);
        _tripStorageMock = new Mock<LocalTripStorage>(_storageMock.Object, "trip1");
        _alertServiceMock = new Mock<IAlertService>();

        _storageMock.Setup(s => s.GetLocalTripStorage(It.IsAny<string>())).Returns(_tripStorageMock.Object);

        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_alertServiceMock.Object);
        Services.AddSingleton<INavigationService>(sp => 
        {
            var navService = new NavigationService();
            navService.Register(sp.GetRequiredService<NavigationManager>());
            return navService;
        });
        }

    [Fact]
    public async Task SelectVersion_ShouldWorkOnlyWithButton()
    {
        // Arrange
        var conflict = new ConflictInfo { Id = "tx1", Label = "Transaction 1", Type = "transaction" };
        var versions = new List<ConflictVersion<Transaction>>
        {
            new ConflictVersion<Transaction> { Author = "Alice", Timestamp = DateTime.UtcNow, Data = new Transaction { Id = "tx1", Amount = 10, Description = "A" } },
            new ConflictVersion<Transaction> { Author = "Bob", Timestamp = DateTime.UtcNow, Data = new Transaction { Id = "tx1", Amount = 20, Description = "B" } }
        };

        _tripStorageMock.Setup(ts => ts.GetConflictingTransactionVersionsAsync("tx1"))
            .ReturnsAsync(versions);
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings { DeviceId = "dev1" });

        var cut = Render<ConflictResolverModal>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Conflict, conflict)
            .Add(p => p.TripSlug, "trip1")
        );

        // Assert - Initial state (nothing selected)
        cut.FindAll(".version-tile.selected").Should().BeEmpty();
        cut.Find(".btn-primary-vibe.w-100").Attributes["disabled"].Should().NotBeNull();

        // Act - Click the tile background (should NOT have a handler anymore)
        var tile = cut.Find(".version-tile");
        Action act = () => tile.Click();
        act.Should().Throw<MissingEventHandlerException>();
        cut.FindAll(".version-tile.selected").Should().BeEmpty();

        // Act - Click the button (should select)
        var button = cut.Find(".btn-select-version-vibe");
        await cut.InvokeAsync(() => button.Click());
        
        // Assert - Tile is now selected
        cut.Find(".version-tile").ClassList.Should().Contain("selected");
        cut.Find(".btn-primary-vibe.w-100").Attributes["disabled"].Should().BeNull();
    }

    [Fact]
    public async Task ClickDiffLabel_ShouldShowAlertWithEvaluationString()
    {
        // Arrange
        var conflict = new ConflictInfo { Id = "tx1", Label = "Transaction 1", Type = "transaction" };
        var date1 = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var date2 = new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Unspecified);
        var versions = new List<ConflictVersion<Transaction>>
        {
            new ConflictVersion<Transaction> { Author = "Alice", Timestamp = DateTime.UtcNow, Data = new Transaction { Id = "tx1", Date = date1, Amount = 10, Timezone = "Europe/Rome" } },
            new ConflictVersion<Transaction> { Author = "Bob", Timestamp = DateTime.UtcNow, Data = new Transaction { Id = "tx1", Date = date2, Amount = 10, Timezone = "Europe/Rome" } }
        };

        _tripStorageMock.Setup(ts => ts.GetConflictingTransactionVersionsAsync("tx1"))
            .ReturnsAsync(versions);
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings { DeviceId = "dev1" });

        var cut = Render<ConflictResolverModal>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Conflict, conflict)
            .Add(p => p.TripSlug, "trip1")
        );

        // Find the red label (DateTime is different and clickable)
        var redLabel = cut.Find(".clickable-diff");
        
        // Act
        await cut.InvokeAsync(() => redLabel.Click());

        // Assert
        var expectedString = $"Data: 01/01/2024 12:00:00<br />Fuso orario: Europe/Rome";
        _alertServiceMock.Verify(s => s.ShowAlertAsync(
            "Dettaglio", 
            expectedString,
            "Chiudi", 
            AlertType.Information,
            "left"), Times.Once);
    }

    [Fact]
    public async Task SelectVersion_ShouldBePreserved_WhenReRenderedWithSameParameters()
    {
        // Arrange
        var conflict = new ConflictInfo { Id = "tx1", Label = "Transaction 1", Type = "transaction" };
        var versions = new List<ConflictVersion<Transaction>>
        {
            new ConflictVersion<Transaction> { Author = "Alice", Timestamp = DateTime.UtcNow, Data = new Transaction { Id = "tx1", Amount = 10, Description = "A" } },
            new ConflictVersion<Transaction> { Author = "Bob", Timestamp = DateTime.UtcNow, Data = new Transaction { Id = "tx1", Amount = 20, Description = "B" } }
        };

        _tripStorageMock.Setup(ts => ts.GetConflictingTransactionVersionsAsync("tx1"))
            .ReturnsAsync(versions);
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings { DeviceId = "dev1" });

        var cut = Render<ConflictResolverModal>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Conflict, conflict)
            .Add(p => p.TripSlug, "trip1")
        );

        // Act - Select a version
        var button = cut.Find(".btn-select-version-vibe");
        await cut.InvokeAsync(() => button.Click());
        
        // Assert - Selected
        cut.Find(".version-tile").ClassList.Should().Contain("selected");

        // Act - Re-render with same parameters
        cut.Render(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Conflict, conflict)
            .Add(p => p.TripSlug, "trip1")
        );

        // Assert - Still selected
        cut.Find(".version-tile").ClassList.Should().Contain("selected");
    }

    [Fact]
    public async Task SelectVersion_Button_ShouldNeverBeDisabled()
    {
        // Arrange
        var conflict = new ConflictInfo { Id = "tx1", Label = "Transaction 1", Type = "transaction" };
        var versions = new List<ConflictVersion<Transaction>>
        {
            new ConflictVersion<Transaction> { Author = "Alice", Timestamp = DateTime.UtcNow, Data = new Transaction { Id = "tx1", Amount = 10 } }
        };

        _tripStorageMock.Setup(ts => ts.GetConflictingTransactionVersionsAsync("tx1"))
            .ReturnsAsync(versions);
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings { DeviceId = "dev1" });

        var cut = Render<ConflictResolverModal>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Conflict, conflict)
        );

        // Assert
        var button = cut.Find(".btn-select-version-vibe");
        button.Attributes["disabled"].Should().BeNull();
    }
}
