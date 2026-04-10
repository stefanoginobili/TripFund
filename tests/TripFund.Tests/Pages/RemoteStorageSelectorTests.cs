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
    private readonly Mock<ISyncService> _syncMock;
    private readonly Mock<IAlertService> _alertMock;

    public RemoteStorageSelectorTests()
    {
        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        _syncMock = new Mock<ISyncService>();
        _alertMock = new Mock<IAlertService>();
        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_syncMock.Object);
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
        cut.FindAll(".provider-item").Should().HaveCount(2);
        cut.Find(".provider-item:nth-child(1) .provider-name").TextContent.Should().Be("Google Drive");
        cut.Find(".provider-item:nth-child(2) .provider-name").TextContent.Should().Be("Git");
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
    public void Selector_Git_ShouldShowForm()
    {
        // Arrange
        var cut = Render<RemoteStorageSelector>(parameters => parameters
            .Add(p => p.IsVisible, true)
        );

        // Act - Click Git
        cut.FindAll(".provider-item")[1].Click();

        // Assert
        cut.Find(".modal-title-vibe").TextContent.Should().Be("Git");
        var labels = cut.FindAll("label.form-label-vibe");
        labels[0].TextContent.Should().Be("URL del repository");
        labels[1].TextContent.Should().Be("PAT (Personal Access Token)");
    }

    [Fact]
    public async Task Home_CreateTrip_ShouldShowSelector_AndNavigate()
    {
        // Arrange
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(new LocalTripRegistry());
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings());
        _syncMock.Setup(s => s.IsRemoteLocationEmptyAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(true);

        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<Home>();

        // Act - Click "Crea viaggio"
        cut.Find(".btn-primary-vibe").Click();

        // Assert - Selector visible
        cut.FindComponent<RemoteStorageSelector>().Instance.IsVisible.Should().BeTrue();

        // Act - Select Git and fill form
        cut.FindAll(".provider-item")[1].Click();
        cut.FindAll("input")[0].Change("https://github.com/test/repo.git");
        cut.FindAll("input")[1].Change("secret_pat");
        cut.Find(".btn-primary-vibe.flex-2").Click();

        // Assert - Navigation
        nav.Uri.Should().Contain("/create-trip");
        nav.Uri.Should().Contain("provider=git");
        nav.Uri.Should().Contain("repository=https%3A%2F%2Fgithub.com%2Ftest%2Frepo.git");
        nav.Uri.Should().Contain("pat=secret_pat");
    }

    [Fact]
    public async Task Home_CreateTrip_NotEmpty_ShouldShowAlert()
    {
        // Arrange
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(new LocalTripRegistry());
        _syncMock.Setup(s => s.IsRemoteLocationEmptyAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(false);

        var cut = Render<Home>();

        // Act - Click "Crea viaggio"
        cut.Find(".btn-primary-vibe").Click();
        
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
        _syncMock.Setup(d => d.GetRemoteTripConfigAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync((TripConfig?)null);

        var cut = Render<Home>();

        // Act - Click "Aggiungi viaggio esistente"
        cut.Find(".btn-secondary-vibe").Click();
        
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
        _syncMock.Setup(d => d.GetRemoteTripConfigAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(remoteConfig);
        _alertMock.Setup(a => a.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), "Conferma", "Annulla"))
            .ReturnsAsync(true);

        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<Home>();

        // Act - Click "Aggiungi viaggio esistente"
        cut.Find(".btn-secondary-vibe").Click();
        
        // Select Git and submit
        cut.FindAll(".provider-item")[1].Click();
        cut.FindAll("input")[0].Change("https://github.com/existing");
        cut.FindAll("input")[1].Change("token");
        cut.Find(".btn-primary-vibe.flex-2").Click();

        // Assert
        _alertMock.Verify(a => a.ConfirmAsync("Conferma", It.Is<string>(s => s.Contains("Remote Trip")), "Conferma", "Annulla"), Times.Once);
        nav.Uri.Should().Contain("/trip/remote-trip");
        
        // Verify trip saved locally
        _storageMock.Verify(s => s.SaveTripConfigAsync("remote-trip", It.IsAny<TripConfig>(), It.IsAny<string>(), false), Times.Once);
        _storageMock.Verify(s => s.SaveTripRegistryAsync(It.Is<LocalTripRegistry>(r => r.Trips.ContainsKey("remote-trip"))), Times.Once);
    }
}
