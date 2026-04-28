using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Pages;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using System.Collections.Generic;
using System.Linq;
using TripFund.App.Constants;
using AngleSharp.Dom;

namespace TripFund.Tests.Pages;

public class ExpensesChartsTests : BunitContext
{
    private readonly Mock<LocalTripStorageService> _storageMock;
    private readonly Mock<IExchangeRateService> _exchangeRatesMock;

    public ExpensesChartsTests()
    {
        var itCulture = new System.Globalization.CultureInfo("it-IT");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = itCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = itCulture;

        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        _exchangeRatesMock = new Mock<IExchangeRateService>();
        
        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_exchangeRatesMock.Object);
        
        JSInterop.SetupVoid("appLogic.lockScroll");
        JSInterop.SetupVoid("appLogic.unlockScroll");
    }

    [Fact]
    public void ExpensesCharts_ShouldRenderGlobalSectionWhenMultipleCurrenciesAvailable()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency>
            {
                { "EUR", new Currency { Symbol = "€" } },
                { "USD", new Currency { Symbol = "$" } }
            }
        };

        var transactions = new List<Transaction>
        {
            new Transaction { Type = "expense", Currency = "EUR", Category = "food", Amount = 100 },
            new Transaction { Type = "expense", Currency = "USD", Category = "food", Amount = 50 }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);
        
        // Mock rates available
        _exchangeRatesMock.Setup(x => x.AreRatesMissingAsync(tripSlug, It.IsAny<IEnumerable<DateTime>>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(false);
        _exchangeRatesMock.Setup(x => x.GetRateAsync(tripSlug, "EUR", It.IsAny<DateTime>()))
            .ReturnsAsync(1.0m);
        _exchangeRatesMock.Setup(x => x.GetRateAsync(tripSlug, "USD", It.IsAny<DateTime>()))
            .ReturnsAsync(0.5m); // 1 EUR = 0.5 USD (very expensive EUR) -> 50 USD = 100 EUR

        // Act
        var cut = Render<ExpensesCharts>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert
        cut.FindAll(".chart-section").Should().HaveCount(3); // Global + EUR + USD
        cut.FindAll(".section-header-vibe").Any(e => e.TextContent.Contains("Spese globali (in EUR)")).Should().BeTrue();
        
        var globalLegend = cut.Find(".global-tile .legend-container");
        globalLegend.InnerHtml.Should().Contain("€ 200,00"); // 100 EUR + 100 EUR equiv
    }

    [Fact]
    public void ExpensesCharts_ShouldShowDownloadButton_WhenRatesAreMissing()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency>
            {
                { "EUR", new Currency { Symbol = "€" } },
                { "USD", new Currency { Symbol = "$" } }
            }
        };

        var transactions = new List<Transaction>
        {
            new Transaction { Type = "expense", Currency = "USD", Category = "food", Amount = 50 }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);
        
        // Mock rates missing
        _exchangeRatesMock.Setup(x => x.AreRatesMissingAsync(tripSlug, It.IsAny<IEnumerable<DateTime>>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(true);

        // Act
        var cut = Render<ExpensesCharts>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert
        cut.Find(".missing-rates-container").Should().NotBeNull();
        cut.Find(".missing-rates-container button").TextContent.Should().Contain("Scarica tassi di conversione mancanti");
    }

    [Fact]
    public void ExpensesCharts_ShouldRenderSectionsForEachCurrency()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } }
        };

        var transactions = new List<Transaction>
        {
            new Transaction { Type = "expense", Currency = "EUR", Category = "food", Amount = 100 }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<ExpensesCharts>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert
        cut.FindAll(".chart-section").Should().HaveCount(1);
        cut.Find(".section-header-vibe").TextContent.Should().Contain("Spese in Euro");
    }

    [Fact]
    public void ExpensesCharts_ShouldHandleCategoriesCorrectly()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } },
            Categories = new TripCategories
            {
                Expenses = new Dictionary<string, ExpenseCategory>
                {
                    { "food", new ExpenseCategory { Name = "Cibo", Icon = "🍔", Color = "#FF0000" } }
                }
            }
        };

        var transactions = new List<Transaction>
        {
            new Transaction { Type = "expense", Currency = "EUR", Category = "food", Amount = 100 },
            new Transaction { Type = "expense", Currency = "EUR", Category = null, Amount = 50 }, // Altro
            new Transaction { Type = "expense", Currency = "EUR", Category = "deleted-cat", Amount = 30 } // Missing from config
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<ExpensesCharts>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert
        var legendItems = cut.FindAll(".legend-item");
        legendItems.Should().HaveCount(3);

        // Cibo
        var foodItem = legendItems.First(i => i.InnerHtml.Contains("Cibo"));
        foodItem.InnerHtml.Should().Contain("🍔");
        foodItem.InnerHtml.Should().Contain("€ 100,00 (56%)");
        foodItem.QuerySelector(".color-swatch")?.GetAttribute("style").Should().Contain("#FF0000");

        // Altro
        var altroItem = legendItems.First(i => i.InnerHtml.Contains(AppConstants.Categories.DefaultName));
        altroItem.InnerHtml.Should().Contain(AppConstants.Categories.DefaultIcon);
        altroItem.InnerHtml.Should().Contain("€ 50,00 (28%)");
        altroItem.QuerySelector(".color-swatch")?.GetAttribute("style").Should().Contain(AppConstants.Categories.DefaultColor);

        // Missing/Deleted
        var missingItem = legendItems.First(i => i.InnerHtml.Contains("deleted-cat"));
        missingItem.InnerHtml.Should().Contain(AppConstants.Categories.UnknownIcon);
        missingItem.InnerHtml.Should().Contain("€ 30,00 (17%)");
        // Gray swatch should be deterministic hex
        var swatchStyle = missingItem.QuerySelector(".color-swatch")?.GetAttribute("style") ?? "";
        swatchStyle.Should().MatchRegex(@"#[0-9A-F]{6}");
    }

    [Fact]
    public void ExpensesCharts_ShouldRenderFullCircleWhenSingleCategory()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } },
            Categories = new TripCategories
            {
                Expenses = new Dictionary<string, ExpenseCategory>
                {
                    { "food", new ExpenseCategory { Name = "Cibo", Icon = "🍔", Color = "#FF0000" } }
                }
            }
        };

        var transactions = new List<Transaction>
        {
            new Transaction { Type = "expense", Currency = "EUR", Category = "food", Amount = 100 }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<ExpensesCharts>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert
        cut.FindAll("circle").Should().HaveCount(1);
        cut.Find("circle").GetAttribute("fill").Should().Be("#FF0000");
        cut.FindAll("path").Should().BeEmpty();
    }
}
