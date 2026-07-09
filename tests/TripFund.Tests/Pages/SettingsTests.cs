using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Pages;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Components;

namespace TripFund.Tests.Pages;

public class SettingsTests : BunitContext
{
    private readonly Mock<LocalStorageService> _storageMock;
    private readonly Mock<IToastService> _toastMock;

    public SettingsTests()
    {
        var itCulture = new System.Globalization.CultureInfo("it-IT");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = itCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = itCulture;

        _storageMock = new Mock<LocalStorageService>("dummy_path");
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings 
        { 
            AuthorName = "Test Author", 
            DeviceId = "test-device-id" 
        });

        _toastMock = new Mock<IToastService>();

        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_toastMock.Object);
        Services.AddSingleton<INavigationService>(sp => 
        {
            var navService = new NavigationService();
            navService.Register(sp.GetRequiredService<NavigationManager>());
            return navService;
        });
    }

    [Fact]
    public void Settings_ShouldRenderCorrectlyWithDescriptions()
    {
        // Act
        var cut = Render<Settings>();

        // Assert
        cut.Find(".header-title").TextContent.Should().Be("Impostazioni");
        
        var helpTexts = cut.FindAll(".help-text");
        helpTexts.Count.Should().Be(2);

        helpTexts[0].TextContent.Should().Contain("Verrà utilizzato nella \"risoluzione dei conflitti\"");
        helpTexts[1].TextContent.Should().Contain("Identificatore di sistema utilizzato per individuare possibili conflitti");
    }

    [Fact]
    public async Task Settings_Save_ShouldStoreSettings()
    {
        // Arrange
        var cut = Render<Settings>();
        var input = cut.Find("input[type='text']");

        // Act
        input.Input("New Author Name");
        cut.Find("button.btn-primary-vibe").Click();

        // Assert
        _storageMock.Verify(s => s.SaveAppSettingsAsync(It.Is<AppSettings>(a => a.AuthorName == "New Author Name" && a.DeviceId == "test-device-id")), Times.Once);
    }
    [Fact]
    public void Settings_Save_EmptyName_ShouldShowToastError()
    {
        // Arrange
        var cut = Render<Settings>();
        var input = cut.Find("input[type='text']");

        // Act
        input.Input("");
        cut.Find("button.btn-primary-vibe").Click();

        // Assert
        _toastMock.Verify(t => t.ShowError("Il nome autore è obbligatorio."), Times.Once);
        _storageMock.Verify(s => s.SaveAppSettingsAsync(It.IsAny<AppSettings>()), Times.Never);
    }
}
