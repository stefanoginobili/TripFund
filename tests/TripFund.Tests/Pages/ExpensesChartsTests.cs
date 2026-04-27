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

    public ExpensesChartsTests()
    {
        var itCulture = new System.Globalization.CultureInfo("it-IT");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = itCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = itCulture;

        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        Services.AddSingleton(_storageMock.Object);
        
        JSInterop.SetupVoid("appLogic.lockScroll");
        JSInterop.SetupVoid("appLogic.unlockScroll");
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
            Currencies = new Dictionary<string, Currency>
            {
                { "EUR", new Currency { Symbol = "€" } },
                { "USD", new Currency { Symbol = "$" } }
            },
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
            new Transaction { Type = "expense", Currency = "USD", Category = "food", Amount = 50 }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<ExpensesCharts>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert
        cut.FindAll(".chart-section").Should().HaveCount(2);
        cut.FindAll(".section-header-vibe").Any(e => e.TextContent.Contains("Spese Cassa EUR")).Should().BeTrue();
        cut.FindAll(".section-header-vibe").Any(e => e.TextContent.Contains("Spese Cassa USD")).Should().BeTrue();
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
        foodItem.InnerHtml.Should().Contain("100");
        foodItem.QuerySelector(".color-swatch")?.GetAttribute("style").Should().Contain("#FF0000");

        // Altro
        var altroItem = legendItems.First(i => i.InnerHtml.Contains(AppConstants.Categories.DefaultName));
        altroItem.InnerHtml.Should().Contain(AppConstants.Categories.DefaultIcon);
        altroItem.InnerHtml.Should().Contain("50");
        altroItem.QuerySelector(".color-swatch")?.GetAttribute("style").Should().Contain(AppConstants.Categories.DefaultColor);

        // Missing/Deleted
        var missingItem = legendItems.First(i => i.InnerHtml.Contains("deleted-cat"));
        missingItem.InnerHtml.Should().Contain(AppConstants.Categories.UnknownIcon);
        missingItem.InnerHtml.Should().Contain("30");
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
