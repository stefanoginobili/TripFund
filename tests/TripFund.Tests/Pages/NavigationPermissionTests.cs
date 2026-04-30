using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Pages;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TripFund.Tests.Pages;

public class NavigationPermissionTests : BunitContext
{
    private readonly Mock<LocalStorageService> _storageMock;
    private readonly Mock<LocalTripStorage> _tripStorageMock;
    private readonly Mock<IAlertService> _alertMock;

    public NavigationPermissionTests()
    {
        var itCulture = new System.Globalization.CultureInfo("it-IT");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = itCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = itCulture;

        _storageMock = new Mock<LocalStorageService>("dummy_path");
        _tripStorageMock = new Mock<LocalTripStorage>(_storageMock.Object, "test-trip");
        _alertMock = new Mock<IAlertService>();
        
        _storageMock.Setup(s => s.GetLocalTripStorage(It.IsAny<string>())).Returns(_tripStorageMock.Object);
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(new LocalTripRegistry());

        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_alertMock.Object);
        Services.AddSingleton(new Mock<IEmailService>().Object);
        Services.AddSingleton(new Mock<IThumbnailService>().Object);
        Services.AddSingleton(new Mock<IRemoteStorageService>().Object);
        Services.AddSingleton(new PdfReportService());
        Services.AddSingleton(new Mock<INativeDatePickerService>().Object);
        Services.AddSingleton<INavigationService>(sp => 
        {
            var navService = new NavigationService();
            navService.Register(sp.GetRequiredService<NavigationManager>());
            return navService;
        });
        
        JSInterop.SetupVoid("appLogic.lockScroll");
        JSInterop.SetupVoid("appLogic.unlockScroll");
        JSInterop.SetupVoid("appLogic.positionMenu", _ => true);
        JSInterop.SetupVoid("appLogic.scrollIntoView", _ => true);
    }

    [Fact]
    public async Task ExpenseEditor_Back_ShouldBeGatedByUnsavedChanges_Veto()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } },
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario" } } }
        };
        _tripStorageMock.Setup(s => s.GetTripConfigAsync()).ReturnsAsync(config);
        _tripStorageMock.Setup(s => s.GetTransactionsAsync()).ReturnsAsync(new List<Transaction>());
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings());

        var navService = Services.GetRequiredService<INavigationService>();
        var navManager = Services.GetRequiredService<NavigationManager>();

        // Set up stack
        await navService.NavigateAsync("/", $"/trip/{tripSlug}/expense");
        var initialUri = navManager.Uri;

        var cut = Render<ExpenseEditor>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act - Make dirty
        cut.Find(".amount-input").Change("10,00");
        
        // Mock alert to return false (Stay)
        _alertMock.Setup(a => a.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AlertType>(), It.IsAny<string>()))
                  .ReturnsAsync(false);

        // Trigger Back
        var handled = await navService.GoBackAsync();

        // Assert
        handled.Should().BeTrue(); // Service handled it by staying
        navManager.Uri.Should().Be(initialUri);
        navService.StackCount.Should().Be(1);
        _alertMock.Verify(a => a.ConfirmAsync(It.Is<string>(s => s.Contains("Modifiche")), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AlertType>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ExpenseEditor_Back_ShouldBeGatedByUnsavedChanges_Proceed()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } },
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario" } } }
        };
        _tripStorageMock.Setup(s => s.GetTripConfigAsync()).ReturnsAsync(config);
        _tripStorageMock.Setup(s => s.GetTransactionsAsync()).ReturnsAsync(new List<Transaction>());
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(new AppSettings());

        var navService = Services.GetRequiredService<INavigationService>();
        var navManager = Services.GetRequiredService<NavigationManager>();

        // Set up stack
        await navService.NavigateAsync("/", $"/trip/{tripSlug}/expense");

        var cut = Render<ExpenseEditor>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act - Make dirty
        cut.Find(".amount-input").Change("10,00");
        
        // Mock alert to return true (Discard)
        _alertMock.Setup(a => a.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AlertType>(), It.IsAny<string>()))
                  .ReturnsAsync(true);

        // Trigger Back
        var handled = await navService.GoBackAsync();

        // Assert
        handled.Should().BeTrue();
        navManager.Uri.Should().Be("http://localhost/");
        navService.StackCount.Should().Be(0);
    }
}
