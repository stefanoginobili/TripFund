using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Common;
using TripFund.App.Components.Pages;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Components;

namespace TripFund.Tests.Pages;

public class RemoteStorageSelectorTests : BunitContext
{
    private readonly Mock<LocalTripStorageService> _storageMock;
    private readonly Mock<IRemoteStorageService> _remoteStorageMock;
    private readonly Mock<IAlertService> _alertMock;

    public RemoteStorageSelectorTests()
    {
        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        _remoteStorageMock = new Mock<IRemoteStorageService>();
        _alertMock = new Mock<IAlertService>();
        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_remoteStorageMock.Object);
        Services.AddSingleton(_alertMock.Object);
    }

    [Fact]
    public void Selector_ShouldRenderProvidersInitially()
    {
        // Act
        var cut = Render<RemoteStorageSelector>(parameters => parameters
            .Add(p => p.IsVisible, true)
        );

        // Assert
        cut.FindAll(".provider-item").Should().HaveCount(1);
        cut.Find(".provider-item:nth-child(1) .provider-name").TextContent.Should().Be("Google Drive");
    }

    [Fact]
    public void Selector_GoogleDrive_ShouldShowForm()
    {
        // Arrange
        var cut = Render<RemoteStorageSelector>(parameters => parameters
            .Add(p => p.IsVisible, true)
        );

        // Act - Click Google Drive
        cut.FindAll(".provider-item")[0].Click();

        // Assert
        cut.Find(".modal-title-vibe").TextContent.Should().Be("Google Drive");
        cut.Find("label.form-label-vibe").TextContent.Should().Be("URL della cartella");
        cut.Find("input.form-control-vibe").Should().NotBeNull();
    }

    [Fact]
    public async Task Home_CreateTrip_ShouldShowSelector_AndNavigate()
    {
        // Arrange
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(new LocalTripRegistry());
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings());
        _remoteStorageMock.Setup(s => s.IsRemoteLocationEmptyAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(true);

        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<Home>();

        // Act - Click "Crea viaggio"
        cut.Find(".action-link").Click();

        // Assert - Selector visible
        cut.FindComponent<RemoteStorageSelector>().Instance.IsVisible.Should().BeTrue();

        // Act - Select Google Drive and fill form
        cut.FindAll(".provider-item")[0].Click();
        cut.Find("input").Change("https://drive.google.com/test");
        cut.Find(".btn-primary-vibe.flex-2").Click();

        // Assert - Navigation
        nav.Uri.Should().Contain("/create-trip");
        nav.Uri.Should().Contain("provider=google-drive");
        nav.Uri.Should().Contain("folderUrl=https%3A%2F%2Fdrive.google.com%2Ftest");
    }

    [Fact]
    public async Task Home_CreateTrip_NotEmpty_ShouldShowAlert()
    {
        // Arrange
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(new LocalTripRegistry());
        _remoteStorageMock.Setup(s => s.IsRemoteLocationEmptyAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(false);

        var cut = Render<Home>();

        // Act - Click "Crea viaggio"
        cut.Find(".action-link").Click();
        
        // Select Google Drive and submit
        cut.FindAll(".provider-item")[0].Click();
        cut.Find("input").Change("https://drive.google.com/not-empty");
        cut.Find(".btn-primary-vibe.flex-2").Click();

        // Assert
        _alertMock.Verify(a => a.ShowAlertAsync("Errore", "La posizione remota deve esistere ed essere vuota.", "OK"), Times.Once);
    }

    [Fact]
    public async Task Home_JoinTrip_NotFound_ShouldShowAlert()
    {
        // Arrange
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(new LocalTripRegistry());
        _remoteStorageMock.Setup(d => d.GetRemoteTripConfigAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync((TripConfig?)null);

        var cut = Render<Home>();

        // Act - Click "Aggiungi viaggio esistente"
        cut.Find(".action-link.secondary").Click();
        
        // Select Google Drive and submit
        cut.FindAll(".provider-item")[0].Click();
        cut.Find("input").Change("https://drive.google.com/wrong");
        cut.Find(".btn-primary-vibe.flex-2").Click();

        // Assert
        _alertMock.Verify(a => a.ShowAlertAsync("Errore", "Impossibile trovare i dati del viaggio nella posizione specificata.", "OK"), Times.Once);
    }

    [Fact]
    public async Task Home_JoinTrip_Found_Confirm_ShouldNavigate()
    {
        // Arrange
        var remoteConfig = new TripConfig { Name = "Remote Trip", StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(1) };
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(new LocalTripRegistry());
        _remoteStorageMock.Setup(d => d.GetRemoteTripConfigAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(remoteConfig);
        _alertMock.Setup(a => a.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), "Conferma", "Annulla"))
            .ReturnsAsync(true);

        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<Home>();

        // Act - Click "Aggiungi viaggio esistente"
        cut.Find(".action-link.secondary").Click();
        
        // Select Google Drive and submit
        cut.FindAll(".provider-item")[0].Click();
        cut.Find("input").Change("https://drive.google.com/existing");
        cut.Find(".btn-primary-vibe.flex-2").Click();

        // Assert
        _alertMock.Verify(a => a.ConfirmAsync("Conferma", It.Is<string>(s => s.Contains("Remote Trip")), "Conferma", "Annulla"), Times.Once);
        nav.Uri.Should().Contain("/trip/remote-trip");
        
        // Verify trip saved locally
        _storageMock.Verify(s => s.SaveTripConfigAsync("remote-trip", It.IsAny<TripConfig>(), It.IsAny<string>(), false), Times.Once);
        _storageMock.Verify(s => s.SaveTripRegistryAsync(It.Is<LocalTripRegistry>(r => r.Trips.ContainsKey("remote-trip"))), Times.Once);
    }
}
