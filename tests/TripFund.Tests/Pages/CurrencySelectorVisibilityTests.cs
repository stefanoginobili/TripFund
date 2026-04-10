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
    private readonly Mock<LocalTripStorageService> _storageMock;
    private readonly Mock<IAlertService> _alertMock;
    private readonly Mock<IEmailService> _emailMock;
    private readonly Mock<INativeDatePickerService> _datePickerMock;
    private readonly Mock<IThumbnailService> _thumbnailMock;

    public CurrencySelectorVisibilityTests()
    {
        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        _alertMock = new Mock<IAlertService>();
        _emailMock = new Mock<IEmailService>();
        _datePickerMock = new Mock<INativeDatePickerService>();
        _thumbnailMock = new Mock<IThumbnailService>();
        
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(new LocalTripRegistry());
        
        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_alertMock.Object);
        Services.AddSingleton(_emailMock.Object);
        Services.AddSingleton(_datePickerMock.Object);
        Services.AddSingleton(_thumbnailMock.Object);
        Services.AddSingleton(new Mock<ISyncService>().Object);

        JSInterop.SetupVoid("headerLogic.scrollIntoView", _ => true);
        JSInterop.SetupVoid("headerLogic.selectText", _ => true);
        
        var settings = new AppSettings { AuthorName = "Test Author", DeviceId = "test-author" };
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(settings);
    }

    [Fact]
    public void AddContribution_ShouldShowRequestedCurrencyWhenTripHasMultipleCurrencies()
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
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);

        // Act - Request USD
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/trip/{tripSlug}/add-contribution?currency=USD");
        
        var cut = Render<AddContribution>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
        );

        // Assert - Should show selector because trip has > 1 currency
        var pills = cut.FindAll(".currency-pill");
        pills.Should().HaveCount(1);
        pills[0].TextContent.Trim().Should().Be("USD");
    }

    [Fact]
    public void AddExpense_ShouldHideCurrencySelectorWhenTripHasOnlyOneCurrency()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Currencies = new Dictionary<string, Currency> 
            { 
                { "EUR", new Currency { Symbol = "€" } }
            },
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario" } } }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);

        // Act - Request EUR
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/trip/{tripSlug}/add-expense?currency=EUR");

        var cut = Render<AddExpense>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
        );

        // Assert - Should NOT show because trip has only 1 currency
        cut.FindAll(".currency-selector-container").Should().BeEmpty();
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
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(new List<Transaction>());

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
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(new List<Transaction>());

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
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(new List<Transaction>());

        // Act
        var cut = Render<MemberDashboard>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
            .Add(p => p.memberSlug, memberSlug)
        );

        // Assert
        cut.FindAll(".currency-selector-container").Should().BeEmpty();
    }

    [Fact]
    public async Task AddContribution_Save_ShouldNavigateBackWithCurrency()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } },
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario" } } }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/trip/{tripSlug}/add-contribution?currency=EUR");

        var cut = Render<AddContribution>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act
        cut.Find(".amount-input").Change("10");
        
        // Open member selector
        cut.Find(".custom-member-selector").Click();
        
        // Select Mario
        var marioItem = cut.FindAll(".dropdown-member-item").First();
        marioItem.Click();

        // Submit
        await cut.Find(".btn-primary-vibe").ClickAsync();

        // Assert
        nav.Uri.Should().Contain($"/trip/{tripSlug}?currency=EUR");
    }

    [Fact]
    public async Task AddContribution_Back_ShouldNavigateToMemberDashboardWhenMemberPresent()
    {
        // Arrange
        var tripSlug = "test-trip";
        var memberSlug = "mario";
        var config = new TripConfig
        {
            Currencies = new Dictionary<string, Currency> { { "USD", new Currency { Symbol = "$" } } },
            Members = new Dictionary<string, User> { { memberSlug, new User { Name = "Mario" } } }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/trip/{tripSlug}/add-contribution?member={memberSlug}&currency=USD");

        var cut = Render<AddContribution>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act
        await cut.Find("header .icon-btn").ClickAsync();

        // Assert
        nav.Uri.Should().Contain($"/trip/{tripSlug}/member/{memberSlug}?currency=USD");
    }
}
