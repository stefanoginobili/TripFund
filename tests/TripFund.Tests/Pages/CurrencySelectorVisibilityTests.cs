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

    public CurrencySelectorVisibilityTests()
    {
        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        _alertMock = new Mock<IAlertService>();
        _emailMock = new Mock<IEmailService>();
        _datePickerMock = new Mock<INativeDatePickerService>();
        
        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_alertMock.Object);
        Services.AddSingleton(_emailMock.Object);
        Services.AddSingleton(_datePickerMock.Object);

        JSInterop.SetupVoid("headerLogic.scrollIntoView", _ => true);
        JSInterop.SetupVoid("headerLogic.selectText", _ => true);
        
        var settings = new AppSettings { AuthorName = "Test Author", DeviceId = "test-author" };
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(settings);
    }

    [Fact]
    public void AddContribution_ShouldOnlyShowRequestedCurrency()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Currencies = new Dictionary<string, Currency> 
            { 
                { "EUR", new Currency { Symbol = "€" } },
                { "USD", new Currency { Symbol = "$" } },
                { "GBP", new Currency { Symbol = "£" } }
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

        // Assert
        var pills = cut.FindAll(".currency-pill");
        pills.Should().HaveCount(1);
        pills[0].TextContent.Trim().Should().Be("USD");
        pills[0].ClassList.Should().Contain("active");
    }

    [Fact]
    public void AddExpense_ShouldOnlyShowRequestedCurrency()
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

        // Act - Request EUR explicitly
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/trip/{tripSlug}/add-expense?currency=EUR");

        var cut = Render<AddExpense>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
        );

        // Assert
        var pills = cut.FindAll(".currency-pill");
        pills.Should().HaveCount(1);
        pills[0].TextContent.Trim().Should().Be("EUR");
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
        await cut.Find(".submit-btn").ClickAsync();

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
