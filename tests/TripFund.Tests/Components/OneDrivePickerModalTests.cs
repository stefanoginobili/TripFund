using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Common;
using TripFund.App.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TripFund.Tests.Components;

public class OneDrivePickerModalTests : BunitContext
{
    private readonly Mock<IOneDrivePickerService> _oneDriveServiceMock;
    private readonly Mock<IAlertService> _alertServiceMock;

    public OneDrivePickerModalTests()
    {
        _oneDriveServiceMock = new Mock<IOneDrivePickerService>();
        _alertServiceMock = new Mock<IAlertService>();

        Services.AddSingleton(_oneDriveServiceMock.Object);
        Services.AddSingleton(_alertServiceMock.Object);
        Services.AddSingleton<INavigationService>(sp => 
        {
            var navService = new NavigationService();
            navService.Register(sp.GetRequiredService<NavigationManager>());
            return navService;
        });
    }

    [Fact]
    public async Task OpenFolder_ShouldNotUpdateBreadcrumb_WhenLoadFails()
    {
        // Arrange
        var accessToken = "test-token";
        var rootItems = new List<OneDriveItem>
        {
            new OneDriveItem { Id = "folder1", Name = "Personal Vault" }
        };

        _oneDriveServiceMock
            .Setup(s => s.ListFoldersAsync(accessToken, null, null))
            .ReturnsAsync(rootItems);

        _oneDriveServiceMock
            .Setup(s => s.ListFoldersAsync(accessToken, "folder1", It.IsAny<string>()))
            .ThrowsAsync(new System.Exception("Access denied (Personal Vault)"));

        var cut = Render<OneDrivePickerModal>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.AccessToken, accessToken)
        );

        // Wait for initial load
        cut.WaitForState(() => !cut.Markup.Contains("spinner-border"));

        // Assert initial breadcrumb
        cut.Find(".breadcrumb-item.active").TextContent.Trim().Should().Be("I miei file");
        cut.FindAll(".breadcrumb-item").Should().HaveCount(1);

        // Act - Click "Apri" on the vault folder
        var openButton = cut.Find(".btn-open");
        await cut.InvokeAsync(() => openButton.Click());

        // Wait for load attempt to complete
        cut.WaitForState(() => !cut.Markup.Contains("spinner-border"));

        // Assert - Breadcrumb should NOT have "Personal Vault"
        cut.FindAll(".breadcrumb-item").Should().HaveCount(1);
        cut.Find(".breadcrumb-item.active").TextContent.Trim().Should().Be("I miei file");

        // Assert - Error message should be visible
        var alert = cut.Find(".alert-error-vibe");
        alert.TextContent.Should().Contain("Errore nel caricamento della cartella: Access denied (Personal Vault)");
    }

    [Fact]
    public async Task OpenFolder_ShouldUpdateBreadcrumb_WhenLoadSucceeds()
    {
        // Arrange
        var accessToken = "test-token";
        var rootItems = new List<OneDriveItem>
        {
            new OneDriveItem { Id = "folder1", Name = "Documents" }
        };
        var folder1Items = new List<OneDriveItem>
        {
            new OneDriveItem { Id = "subfolder1", Name = "Work" }
        };

        _oneDriveServiceMock
            .Setup(s => s.ListFoldersAsync(accessToken, null, null))
            .ReturnsAsync(rootItems);

        _oneDriveServiceMock
            .Setup(s => s.ListFoldersAsync(accessToken, "folder1", It.IsAny<string>()))
            .ReturnsAsync(folder1Items);

        var cut = Render<OneDrivePickerModal>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.AccessToken, accessToken)
        );

        // Wait for initial load
        cut.WaitForState(() => !cut.Markup.Contains("spinner-border"));

        // Act - Click "Apri" on the Documents folder
        var openButton = cut.Find(".btn-open");
        await cut.InvokeAsync(() => openButton.Click());

        // Wait for load attempt to complete
        cut.WaitForState(() => !cut.Markup.Contains("spinner-border"));

        // Assert - Breadcrumb should have "Documents"
        var breadcrumbItems = cut.FindAll(".breadcrumb-item");
        breadcrumbItems.Should().HaveCount(2);
        breadcrumbItems[0].TextContent.Trim().Should().Be("I miei file");
        breadcrumbItems[1].TextContent.Trim().Should().Be("Documents");
        breadcrumbItems[1].ClassList.Should().Contain("active");
    }

    [Fact]
    public async Task OneDrivePicker_ShouldResetState_WhenReopened()
    {
        // Arrange
        var accessToken = "test-token";
        var rootItems = new List<OneDriveItem>
        {
            new OneDriveItem { Id = "folder1", Name = "Documents" }
        };
        var folder1Items = new List<OneDriveItem>
        {
            new OneDriveItem { Id = "subfolder1", Name = "Work" }
        };

        _oneDriveServiceMock
            .Setup(s => s.ListFoldersAsync(accessToken, null, null))
            .ReturnsAsync(rootItems);

        _oneDriveServiceMock
            .Setup(s => s.ListFoldersAsync(accessToken, "folder1", It.IsAny<string>()))
            .ReturnsAsync(folder1Items);

        var cut = Render<OneDrivePickerModal>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.AccessToken, accessToken)
        );

        // Wait for initial load
        cut.WaitForState(() => !cut.Markup.Contains("spinner-border"));

        // Act - Open a folder and select an item
        var openButton = cut.Find(".btn-open");
        await cut.InvokeAsync(() => openButton.Click());
        cut.WaitForState(() => !cut.Markup.Contains("spinner-border"));
        
        // Select an item in the subfolder
        var row = cut.Find(".item-row");
        await cut.InvokeAsync(() => row.Click());
        
        // Assert - Selection is active
        cut.Find(".item-row").ClassList.Should().Contain("selected");
        cut.Find(".btn-primary-vibe").Attributes["disabled"].Should().BeNull();

        // Act - Close modal
        cut.Render(parameters => parameters.Add(p => p.IsVisible, false));
        
        // Act - Reopen modal
        cut.Render(parameters => parameters.Add(p => p.IsVisible, true));

        // Wait for reload
        cut.WaitForState(() => !cut.Markup.Contains("spinner-border"));

        // Assert - State should be reset
        cut.Find(".breadcrumb-item.active").TextContent.Trim().Should().Be("I miei file");
        cut.FindAll(".breadcrumb-item").Should().HaveCount(1);
        cut.Find(".btn-primary-vibe").Attributes["disabled"].Should().NotBeNull();
        cut.FindAll(".item-row.selected").Should().BeEmpty();
    }
}
