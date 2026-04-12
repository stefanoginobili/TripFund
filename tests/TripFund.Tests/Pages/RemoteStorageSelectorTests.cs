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

    public RemoteStorageSelectorTests()
    {
        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        _remoteStorageMock = new Mock<IRemoteStorageService>();
        _alertMock = new Mock<IAlertService>();
        
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
        Services.AddSingleton(new Mock<IOneDrivePickerService>().Object);
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
        cut.Find(".provider-item:nth-child(1) .provider-name").TextContent.Should().Be("Memoria Locale");
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
}
