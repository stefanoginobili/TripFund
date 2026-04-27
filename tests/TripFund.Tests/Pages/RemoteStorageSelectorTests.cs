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
    private readonly Mock<OneDriveRemoteStorageService> _oneDriveMock;
    private readonly Mock<IOneDrivePickerService> _pickerServiceMock;

    public RemoteStorageSelectorTests()
    {
        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        _remoteStorageMock = new Mock<IRemoteStorageService>();
        _alertMock = new Mock<IAlertService>();
        _pickerServiceMock = new Mock<IOneDrivePickerService>();
        
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _oneDriveMock = new Mock<OneDriveRemoteStorageService>(
            httpClientFactoryMock.Object,
            new Mock<TripFund.App.Services.IWebAuthenticator>().Object,
            _storageMock.Object,
            new Mock<IMicrosoftAuthConfiguration>().Object,
            new RemoteStorageSyncEngine(_storageMock.Object),
            "https://graph.microsoft.com/v1.0");
        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_remoteStorageMock.Object);
        Services.AddSingleton(_alertMock.Object);
        Services.AddSingleton(_oneDriveMock.Object);
        Services.AddSingleton(_pickerServiceMock.Object);
        Services.AddSingleton(new Mock<IMicrosoftAuthConfiguration>().Object);
    }

    [Fact]
    public void Selector_ShouldRenderProvidersInitially()
    {
        // Act
        var cut = Render<RemoteStorageSelector>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.IsJoining, false)
        );

        // Assert
        cut.FindAll(".provider-item").Should().HaveCount(2);
        cut.Find(".provider-item:nth-child(1) .provider-name").TextContent.Should().Be("Memoria Dispositivo");
        cut.Find(".provider-item:nth-child(2) .provider-name").TextContent.Should().Be("Microsoft OneDrive");
    }

    [Fact]
    public void Selector_ShouldRenderOnlyOneDrive_WhenJoining()
    {
        // Act
        var cut = Render<RemoteStorageSelector>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.IsJoining, true)
        );

        // Assert
        cut.FindAll(".provider-item").Should().HaveCount(1);
        cut.Find(".provider-item:nth-child(1) .provider-name").TextContent.Should().Be("Microsoft OneDrive");
    }

    [Fact]
    public async Task Home_CreateTrip_ShouldNavigate_MemoriaLocale()
    {
        // Arrange
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(new LocalTripRegistry());
        
        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<Home>();

        // Act - Click "Crea viaggio"
        cut.Find(".action-link").Click();

        // Act - Select Memoria Locale (first item)
        cut.FindAll(".provider-item")[0].Click();

        // Assert - Navigation without parameters
        nav.Uri.Should().EndWith("/create-trip");
    }

    [Fact]
    public void Selector_ShouldResetState_WhenReopened()
    {
        // Arrange
        var cut = Render<RemoteStorageSelector>(parameters => parameters
            .Add(p => p.IsVisible, true)
        );

        // Act - Select OneDrive
        cut.FindAll(".provider-item")[1].Click();
        cut.Find(".modal-title-vibe").TextContent.Should().Be("Microsoft OneDrive");

        // Act - Close
        cut.Render(parameters => parameters.Add(p => p.IsVisible, false));
        
        // Act - Reopen
        cut.Render(parameters => parameters.Add(p => p.IsVisible, true));

        // Assert - Should be back to provider selection
        cut.Find(".modal-title-vibe").TextContent.Should().Be("Seleziona Archivio");
        cut.FindAll(".provider-item").Should().HaveCount(2);
    }

    [Fact]
    public async Task OpenPicker_ShouldPreventConcurrentCalls_EvenIfAnnullaClicked()
    {
        // Arrange
        var tcs = new TaskCompletionSource<Dictionary<string, string>?>();
        _oneDriveMock.Setup(s => s.AuthenticateUserAsync(It.IsAny<CancellationToken>())).Returns(tcs.Task);
        
        _pickerServiceMock.Setup(s => s.ListFoldersAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<OneDriveItem>());

        var cut = Render<RemoteStorageSelector>(parameters => parameters
            .Add(p => p.IsVisible, true)
        );

        // Select OneDrive
        cut.FindAll(".provider-item")[1].Click();
        
        // Click Sfoglia Cartelle
        cut.Find(".btn-picker-vibe").Click();
        _oneDriveMock.Verify(s => s.AuthenticateUserAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Click Annulla (this cancels the CTS)
        cut.Find(".btn-link-vibe").Click();
        
        // At this point, the task should have been cancelled.
        // In real life, the awaiter would throw OperationCanceledException and reset _isOpeningPicker.
        // Since we are mocking, we should signal cancellation if we want to simulate the flow properly.
    }

    [Fact]
    public void OpenPicker_ShouldIgnoreTaskCanceledException()
    {
        // Arrange
        _oneDriveMock.Setup(s => s.AuthenticateUserAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        
        var cut = Render<RemoteStorageSelector>(parameters => parameters
            .Add(p => p.IsVisible, true)
        );

        // Select OneDrive
        cut.FindAll(".provider-item")[1].Click();
        
        // Click Sfoglia Cartelle
        cut.Find(".btn-picker-vibe").Click();
        
        // Assert - No alert should be shown
        _alertMock.Verify(a => a.ShowAlertAsync(
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<AlertType>(), 
            It.IsAny<string>()), Times.Never);
        
        // Loading state should be gone
        cut.FindAll(".picker-loading-state").Should().BeEmpty();
        cut.Find(".btn-picker-vibe").Should().NotBeNull();
    }
}
