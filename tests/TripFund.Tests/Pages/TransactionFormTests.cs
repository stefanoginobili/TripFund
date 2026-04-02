using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Pages;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Components;
using AngleSharp.Dom;

namespace TripFund.Tests.Pages;

public class TransactionFormTests : BunitContext
{
    private readonly Mock<LocalTripStorageService> _storageMock;

    public TransactionFormTests()
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        Services.AddSingleton(_storageMock.Object);
        
        // Mock AppSettings
        var settings = new AppSettings { AuthorName = "Test Author", DeviceId = "test-author" };
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(settings);
    }

    [Fact]
    public void AddExpense_AutoSplit_ShouldDivideEqually()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } },
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Avatar = "M" } },
                { "luigi", new User { Name = "Luigi", Avatar = "L" } },
                { "carlo", new User { Name = "Carlo", Avatar = "C" } }
            }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);

        var cut = Render<AddExpense>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act - Set amount to 30.00
        cut.Find(".amount-input").Input("30.00");

        // Assert - Each of the 3 members should have 10.00
        var splitInputs = cut.FindAll(".split-amount-input");
        splitInputs.Should().HaveCount(3);
        foreach (var input in splitInputs)
        {
            input.GetAttribute("value").Should().Be("10");
        }
    }

    [Fact]
    public void AddExpense_MixedSplit_ShouldCalculateCorrectly()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } },
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Avatar = "M" } },
                { "luigi", new User { Name = "Luigi", Avatar = "L" } },
                { "carlo", new User { Name = "Carlo", Avatar = "C" } }
            }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);

        var cut = Render<AddExpense>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act
        // 1. Set total amount to 100
        cut.Find(".amount-input").Input("100");

        // 2. Set Mario to Manual and give him 50
        var marioRow = cut.FindAll(".member-split-row").First(r => r.InnerHtml.Contains("Mario"));
        marioRow.QuerySelector(".split-mode-toggle")!.Click(); 
        
        // Re-find Mario row because of re-render
        marioRow = cut.FindAll(".member-split-row").First(r => r.InnerHtml.Contains("Mario"));
        marioRow.QuerySelector(".split-amount-input")!.Input("50");

        // Assert
        // Luigi and Carlo (Auto) should share the remaining 50 -> 25 each
        var luigiRow = cut.FindAll(".member-split-row").First(r => r.InnerHtml.Contains("Luigi"));
        var carloRow = cut.FindAll(".member-split-row").First(r => r.InnerHtml.Contains("Carlo"));

        luigiRow.QuerySelector(".split-amount-input")!.GetAttribute("value").Should().Be("25");
        carloRow.QuerySelector(".split-amount-input")!.GetAttribute("value").Should().Be("25");
    }

    [Fact]
    public void AddExpense_ExcludeMember_ShouldDistributeAmongOthers()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } },
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Avatar = "M" } },
                { "luigi", new User { Name = "Luigi", Avatar = "L" } }
            }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);

        var cut = Render<AddExpense>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act
        cut.Find(".amount-input").Input("100");
        
        // Exclude Mario
        var marioRow = cut.FindAll(".member-split-row").First(r => r.InnerHtml.Contains("Mario"));
        marioRow.QuerySelector(".switch")!.Click();

        // Assert
        // Luigi should take the full 100
        var luigiRow = cut.FindAll(".member-split-row").First(r => r.InnerHtml.Contains("Luigi"));
        luigiRow.QuerySelector(".split-amount-input")!.GetAttribute("value").Should().Be("100");
        
        // Mario row should NOT have split controls anymore
        marioRow = cut.FindAll(".member-split-row").First(r => r.InnerHtml.Contains("Mario"));
        marioRow.QuerySelectorAll(".split-controls").Should().BeEmpty();
    }

    [Fact]
    public void AddExpense_Rounding_ShouldHandleRemainder()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } },
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Avatar = "M" } },
                { "luigi", new User { Name = "Luigi", Avatar = "L" } },
                { "carlo", new User { Name = "Carlo", Avatar = "C" } }
            }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);

        var cut = Render<AddExpense>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act - Set amount to 10.00
        // 10 / 3 = 3.3333...
        // One should get 3.34, others 3.33
        cut.Find(".amount-input").Input("10.00");

        // Assert
        var splitInputs = cut.FindAll(".split-amount-input");
        var values = splitInputs.Select(i => decimal.Parse(i.GetAttribute("value")!, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        
        values.Sum().Should().Be(10.00m);
        values.Should().Contain(3.34m);
        values.Count(v => v == 3.33m).Should().Be(2);
    }
}
