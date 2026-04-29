using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Pages;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using System.Collections.Generic;
using Microsoft.AspNetCore.Components;
using System.Linq;

namespace TripFund.Tests.Pages;

public class CurrencySelectorVisibilityTests : BunitContext
{
    private readonly Mock<LocalStorageService> _storageMock;
    private readonly Mock<LocalTripStorage> _tripStorageMock;
    private readonly Mock<IAlertService> _alertMock;
    private readonly Mock<IEmailService> _emailMock;
    private readonly Mock<INativeDatePickerService> _datePickerMock;
    private readonly Mock<IThumbnailService> _thumbnailMock;

    public CurrencySelectorVisibilityTests()
    {
        _storageMock = new Mock<LocalStorageService>("dummy_path");
        _tripStorageMock = new Mock<LocalTripStorage>(_storageMock.Object, "test-trip");
        _alertMock = new Mock<IAlertService>();
        _emailMock = new Mock<IEmailService>();
        _datePickerMock = new Mock<INativeDatePickerService>();
        _thumbnailMock = new Mock<IThumbnailService>();
        
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(new LocalTripRegistry());
        _storageMock.Setup(s => s.GetLocalTripStorage(It.IsAny<string>())).Returns(_tripStorageMock.Object);
        
        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_alertMock.Object);
        Services.AddSingleton(_emailMock.Object);
        Services.AddSingleton(_datePickerMock.Object);
        Services.AddSingleton(_thumbnailMock.Object);
        Services.AddSingleton(new Mock<IRemoteStorageService>().Object);
        Services.AddSingleton(new PdfReportService());

        JSInterop.SetupVoid("appLogic.scrollIntoView", _ => true);
        JSInterop.SetupVoid("appLogic.selectText", _ => true);
        JSInterop.SetupVoid("appLogic.lockScroll");
        JSInterop.SetupVoid("appLogic.unlockScroll");
        JSInterop.SetupVoid("appLogic.positionMenu", _ => true);
        
        var settings = new AppSettings { AuthorName = "Test Author", DeviceId = "test-author" };
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(settings);
    }

    [Fact]
    public void ContributionEditor_ShouldNOTShowCurrencySelectorEvenWithMultipleCurrencies()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Currencies = new Dictionary<string, Currency> 
            { 
                { "EUR", new Currency { Symbol = "€" } },
                { "USD", new Currency { Symbol = "$" } }
            },
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario" } } }
        };
        _tripStorageMock.Setup(ts => ts.GetTripConfigAsync()).ReturnsAsync(config);

        // Act - Request USD
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/trip/{tripSlug}/contribution?currency=USD");
        
        var cut = Render<ContributionEditor>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
        );

        // Assert - Should NOT show selector anymore
        cut.FindAll(".currency-selector-container").Should().BeEmpty();
        
        // Assert - Should show currency code in amount section
        cut.Find(".currency-symbol").TextContent.Trim().Should().Be("USD");
    }

    [Fact]
    public void ExpenseEditor_ShouldNOTShowCurrencySelectorEvenWithMultipleCurrencies()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Currencies = new Dictionary<string, Currency> 
            { 
                { "EUR", new Currency { Symbol = "€" } },
                { "USD", new Currency { Symbol = "$" } }
            },
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario" } } }
        };
        _tripStorageMock.Setup(ts => ts.GetTripConfigAsync()).ReturnsAsync(config);

        // Act - Request EUR
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/trip/{tripSlug}/expense?currency=EUR");

        var cut = Render<ExpenseEditor>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
        );

        // Assert - Should NOT show selector anymore
        cut.FindAll(".currency-selector-container").Should().BeEmpty();
        
        // Assert - Should show currency code in amount section
        cut.Find(".currency-symbol").TextContent.Trim().Should().Be("EUR");
    }

    [Fact]
    public void TripDashboard_ShouldNOTShowCurrencySelectorWhenOnlyOneCurrencyConfigured()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } },
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario" } } }
        };
        _tripStorageMock.Setup(ts => ts.GetTripConfigAsync()).ReturnsAsync(config);
        _tripStorageMock.Setup(ts => ts.GetTransactionsAsync()).ReturnsAsync(new List<Transaction>());

        // Act
        var cut = Render<TripDashboard>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert
        cut.FindAll(".currency-selector-container").Should().BeEmpty();
    }

    [Fact]
    public void TripDashboard_ShouldShowCurrencySelectorWhenMultipleCurrenciesConfigured()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Currencies = new Dictionary<string, Currency> 
            { 
                { "EUR", new Currency { Symbol = "€" } },
                { "USD", new Currency { Symbol = "$" } }
            },
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario" } } }
        };
        _tripStorageMock.Setup(ts => ts.GetTripConfigAsync()).ReturnsAsync(config);
        _tripStorageMock.Setup(ts => ts.GetTransactionsAsync()).ReturnsAsync(new List<Transaction>());

        // Act
        var cut = Render<TripDashboard>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert
        cut.FindAll(".currency-selector-container").Should().NotBeEmpty();
        cut.FindAll(".currency-pill").Should().HaveCount(2);
    }

    [Fact]
    public void MemberDashboard_ShouldNOTShowCurrencySelectorWhenOnlyOneCurrencyConfigured()
    {
        // Arrange
        var tripSlug = "test-trip";
        var memberSlug = "mario";
        var config = new TripConfig
        {
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } },
            Members = new Dictionary<string, User> { { memberSlug, new User { Name = "Mario" } } }
        };
        _tripStorageMock.Setup(ts => ts.GetTripConfigAsync()).ReturnsAsync(config);
        _tripStorageMock.Setup(ts => ts.GetTransactionsAsync()).ReturnsAsync(new List<Transaction>());

        // Act
        var cut = Render<MemberDashboard>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
            .Add(p => p.memberSlug, memberSlug)
        );

        // Assert
        cut.FindAll(".currency-selector-container").Should().BeEmpty();
    }

    [Fact]
    public async Task ContributionEditor_Save_ShouldNavigateBackWithCurrency()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } },
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario" } } }
        };
        _tripStorageMock.Setup(ts => ts.GetTripConfigAsync()).ReturnsAsync(config);
        
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/trip/{tripSlug}/contribution?currency=EUR");

        var cut = Render<ContributionEditor>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act
        cut.Find(".amount-input").Change("10");
        
        // Open member selector
        cut.Find(".custom-member-selector").Click();
        
        // Select Mario
        var marioItem = cut.FindAll(".dropdown-item-vibe").First();
        marioItem.Click();

        // Submit
        await cut.Find(".btn-primary-vibe").ClickAsync();

        // Assert
        nav.Uri.Should().Contain($"/trip/{tripSlug}?currency=EUR");
    }

    [Fact]
    public async Task ContributionEditor_Back_ShouldNavigateToMemberDashboardWhenMemberPresent()
    {
        // Arrange
        var tripSlug = "test-trip";
        var memberSlug = "mario";
        var config = new TripConfig
        {
            Currencies = new Dictionary<string, Currency> { { "USD", new Currency { Symbol = "$" } } },
            Members = new Dictionary<string, User> { { memberSlug, new User { Name = "Mario" } } }
        };
        _tripStorageMock.Setup(ts => ts.GetTripConfigAsync()).ReturnsAsync(config);
        
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/trip/{tripSlug}/contribution?member={memberSlug}&currency=USD");

        var cut = Render<ContributionEditor>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act
        await cut.Find("header .icon-btn").ClickAsync();

        // Assert
        nav.Uri.Should().Contain($"/trip/{tripSlug}/member/{memberSlug}?currency=USD");
    }
}
