using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Pages;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using System.Collections.Generic;
using System.Linq;

namespace TripFund.Tests.Pages;

public class NavigationFlowTests : BunitContext
{
    private readonly Mock<LocalStorageService> _storageMock;
    private readonly Mock<LocalTripStorage> _tripStorageMock;
    private readonly Mock<IAlertService> _alertMock;

    public NavigationFlowTests()
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
    public async Task TripDashboard_To_Member_And_Back_ShouldPreserveCurrency()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> 
            { 
                { "EUR", new Currency { Symbol = "€", Name = "Euro" } },
                { "USD", new Currency { Symbol = "$", Name = "Dollar" } }
            },
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Avatar = "👤" } }
            }
        };
        _tripStorageMock.Setup(s => s.GetTripConfigAsync()).ReturnsAsync(config);
        _tripStorageMock.Setup(s => s.GetTransactionsAsync()).ReturnsAsync(new List<Transaction>());

        var navService = Services.GetRequiredService<INavigationService>();
        var navManager = Services.GetRequiredService<NavigationManager>();

        // Step 1: Open TripDashboard (defaults to EUR)
        var cut = Render<TripDashboard>(parameters => parameters.Add(p => p.tripSlug, tripSlug));
        
        // Step 2: Select USD
        // Find currency selector and click USD
        var usdBtn = cut.FindAll(".currency-pill").FirstOrDefault(b => b.TextContent.Contains("USD"));
        usdBtn.Should().NotBeNull();
        usdBtn!.Click();
        
        // Verify state
        cut.Instance.GetType().GetField("selectedCurrency", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(cut.Instance).Should().Be("USD");

        // Step 3: Navigate to MemberDashboard
        // We simulate the click that calls NavService.NavigateAsync
        // In the component: @onclick='() => NavService.NavigateAsync($"/trip/{tripSlug}?currency={selectedCurrency}", $"/trip/{tripSlug}/member/{slug}?currency={selectedCurrency}")'
        // We can just call it via the instance or find the button in MembersModal if we were to open it, 
        // but for this integration test we want to see the stack behavior.
        
        var currentUrl = $"/trip/{tripSlug}?currency=USD";
        var targetUrl = $"/trip/{tripSlug}/member/mario?currency=USD";
        
        await navService.NavigateAsync(currentUrl, targetUrl);
        navManager.Uri.Should().Be("http://localhost" + targetUrl);
        navService.StackCount.Should().Be(1);

        // Step 4: Go Back
        var handled = await navService.GoBackAsync();
        handled.Should().BeTrue();
        
        // Assert: We are back at TripDashboard with currency=USD
        navManager.Uri.Should().Be("http://localhost" + currentUrl);
        navService.StackCount.Should().Be(0);
    }
}
