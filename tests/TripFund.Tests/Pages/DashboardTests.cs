using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Pages;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using System.Collections.Generic;
using System.Linq;

namespace TripFund.Tests.Pages;

public class DashboardTests : BunitContext
{
    private readonly Mock<LocalTripStorageService> _storageMock;

    public DashboardTests()
    {
        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        Services.AddSingleton(_storageMock.Object);
    }

    [Fact]
    public void TripDashboard_ShouldCalculateAndDisplayMathCorrectly()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency>
            {
                { "EUR", new Currency { Symbol = "€", ExpectedQuotaPerMember = 500 } },
                { "USD", new Currency { Symbol = "$", ExpectedQuotaPerMember = 600 } }
            },
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Avatar = "M" } },
                { "luigi", new User { Name = "Luigi", Avatar = "L" } }
            }
        };

        var transactions = new List<Transaction>
        {
            new Transaction
            {
                Id = "t1",
                Type = "contribution",
                Currency = "EUR",
                Amount = 300,
                Split = new Dictionary<string, SplitInfo> { { "mario", new SplitInfo { Amount = 300, Manual = true } } }
            },
            new Transaction
            {
                Id = "t2",
                Type = "expense",
                Currency = "EUR",
                Amount = 100,
                Split = new Dictionary<string, SplitInfo> { { "mario", new SplitInfo { Amount = 50, Manual = false } }, { "luigi", new SplitInfo { Amount = 50, Manual = false } } }
            },
            new Transaction
            {
                Id = "t3",
                Type = "contribution",
                Currency = "USD",
                Amount = 100,
                Split = new Dictionary<string, SplitInfo> { { "luigi", new SplitInfo { Amount = 100, Manual = true } } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<TripDashboard>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert (EUR is default)
        // EUR target = 500 * 2 = 1000
        // Mario EUR: Contributed 300, Expense 50 -> Remaining 250
        // Luigi EUR: Contributed 0, Expense 50 -> Remaining -50
        // Total Contributed EUR = 300
        // Total Remaining EUR = 200

        var summaryTotal = cut.Find(".summary-total").TextContent;
        summaryTotal.Should().Contain("200"); // Saldo

        var summarySub = cut.Find(".summary-sub").TextContent;
        summarySub.Should().Contain("300"); // Totale versato

        var memberRows = cut.FindAll(".member-row");
        memberRows.Should().HaveCount(2);

        var marioRow = memberRows.First(r => r.InnerHtml.Contains("Mario"));
        marioRow.InnerHtml.Should().Contain("300"); // Versato
        marioRow.InnerHtml.Should().Contain("250"); // Rimasto

        var luigiRow = memberRows.First(r => r.InnerHtml.Contains("Luigi"));
        luigiRow.InnerHtml.Should().Contain("0"); // Versato
        luigiRow.InnerHtml.Should().Contain("-50"); // Rimasto

        // Act - Switch to USD
        var usdBtn = cut.FindAll(".currency-pill").First(b => b.TextContent.Contains("USD"));
        usdBtn.Click();

        // Assert (USD)
        // USD target = 600 * 2 = 1200
        // Luigi USD: Contributed 100, Expense 0 -> Remaining 100
        // Mario USD: Contributed 0, Expense 0 -> Remaining 0
        // Total Contributed USD = 100
        // Total Remaining USD = 100

        summaryTotal = cut.Find(".summary-total").TextContent;
        summaryTotal.Should().Contain("100");

        marioRow = cut.FindAll(".member-row").First(r => r.InnerHtml.Contains("Mario"));
        marioRow.InnerHtml.Should().Contain("0"); // Versato
    }

    [Fact]
    public void TripDashboard_ShouldShowEccedenzaWhenOverBudget()
    {
        // Arrange
        var tripSlug = "over-trip";
        var config = new TripConfig
        {
            Id = "1",
            Name = "Over Trip",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { ExpectedQuotaPerMember = 100 } } },
            Members = new Dictionary<string, User> { { "m", new User { Name = "M" } } }
        };
        var transactions = new List<Transaction>
        {
            new Transaction { Type = "contribution", Currency = "EUR", Amount = 150, Split = new Dictionary<string, SplitInfo> { { "m", new SplitInfo { Amount = 150 } } } }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<TripDashboard>(p => p.Add(x => x.tripSlug, tripSlug));

        // Assert
        cut.Find(".summary-label").TextContent.Should().Be("Eccedenza");
        cut.Find(".progress-bar-fill").ClassName.Should().Contain("warning");
    }

    [Fact]
    public void TripDashboard_ShouldDisplayMissingMembersAtTheBottomWithGreyScale()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency>
            {
                { "EUR", new Currency { Symbol = "€", ExpectedQuotaPerMember = 500 } }
            },
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Avatar = "M" } }
            }
        };

        var transactions = new List<Transaction>
        {
            new Transaction
            {
                Id = "t1",
                Type = "contribution",
                Currency = "EUR",
                Amount = 100,
                Split = new Dictionary<string, SplitInfo> { { "missing-user", new SplitInfo { Amount = 100, Manual = true } } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<TripDashboard>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert
        var memberRows = cut.FindAll(".member-row");
        memberRows.Should().HaveCount(2);

        // Mario is first
        memberRows[0].InnerHtml.Should().Contain("Mario");
        memberRows[0].ClassName.Should().NotContain("missing-member");

        // Missing user is second
        memberRows[1].InnerHtml.Should().Contain("missing-user");
        memberRows[1].InnerHtml.Should().Contain("❓");
        memberRows[1].ClassName.Should().Contain("missing-member");
    }

    [Fact]
    public void TripDashboard_ShouldFormatAmountsWithConfiguredDecimals()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency>
            {
                { "JPY", new Currency { Symbol = "¥", Decimals = 0, ExpectedQuotaPerMember = 100000 } }
            },
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario" } } }
        };

        var transactions = new List<Transaction>
        {
            new Transaction
            {
                Id = "t1",
                Type = "contribution",
                Currency = "JPY",
                Amount = 50000.75m,
                Split = new Dictionary<string, SplitInfo> { { "mario", new SplitInfo { Amount = 50000.75m, Manual = true } } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<TripDashboard>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // JPY should show 50,001 or 50.001 (rounded by N0) depending on locale
        var summarySub = cut.Find(".summary-sub").TextContent; // Totale versato

        // We check for absence of decimal part
        summarySub.Should().NotContain(".75");
        summarySub.Should().NotContain(",75");

        // It should contain 50,001 or 50.001. We use a regex for flexibility
        summarySub.Should().MatchRegex(@"50[.,]001");

        var txRow = cut.Find(".transaction-row");
        txRow.InnerHtml.Should().MatchRegex(@"¥ 50[.,]001");
        // We ensure it doesn't have decimal digits like .00 or .75
        txRow.InnerHtml.Should().NotMatchRegex(@"50[.,]001[.,]\d+");
    }

    [Fact]
    public void TripDashboard_TransactionsList_ShouldFilterByCurrencyAndSortByDate()
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
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario" } } }
        };

        var now = DateTime.UtcNow;
        var transactions = new List<Transaction>
        {
            new Transaction { Id = "20260404T080000Z-12345678", Currency = "EUR", Date = now.AddHours(-2), Amount = 10, Description = "Old EUR", Split = new Dictionary<string, SplitInfo>() },
            new Transaction { Id = "20260404T090000Z-87654321", Currency = "EUR", Date = now.AddHours(-1), Amount = 20, Description = "New EUR", Split = new Dictionary<string, SplitInfo>() },
            new Transaction { Id = "20260404T100000Z-11223344", Currency = "USD", Date = now, Amount = 30, Description = "USD TX", Split = new Dictionary<string, SplitInfo>() }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<TripDashboard>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert - Default EUR
        var eurTxRows = cut.FindAll(".transaction-row");
        eurTxRows.Should().HaveCount(2);
        eurTxRows[0].InnerHtml.Should().Contain("New EUR"); // Sorted DESC
        eurTxRows[1].InnerHtml.Should().Contain("Old EUR");
        eurTxRows.Any(r => r.InnerHtml.Contains("USD TX")).Should().BeFalse();

        // Act - Switch to USD
        var usdBtn = cut.FindAll(".currency-pill").First(b => b.TextContent.Contains("USD"));
        usdBtn.Click();

        // Assert - USD
        var usdTxRows = cut.FindAll(".transaction-row");
        usdTxRows.Should().HaveCount(1);
        usdTxRows[0].InnerHtml.Should().Contain("USD TX");
        usdTxRows.Any(r => r.InnerHtml.Contains("EUR")).Should().BeFalse();
    }

    [Fact]
    public void MemberDashboard_ShouldCalculateAndDisplayMathCorrectly()
    {
        // Arrange
        var tripSlug = "test-trip";
        var memberSlug = "mario";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency>
            {
                { "EUR", new Currency { Symbol = "€", ExpectedQuotaPerMember = 500 } }
            },
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Avatar = "M" } },
                { "luigi", new User { Name = "Luigi", Avatar = "L" } }
            }
        };

        var transactions = new List<Transaction>
        {
            new Transaction
            {
                Id = "t1",
                Type = "contribution",
                Currency = "EUR",
                Amount = 300,
                Split = new Dictionary<string, SplitInfo> { { "mario", new SplitInfo { Amount = 300, Manual = true } } }
            },
            new Transaction
            {
                Id = "t2",
                Type = "expense",
                Currency = "EUR",
                Amount = 100,
                Split = new Dictionary<string, SplitInfo> { { "mario", new SplitInfo { Amount = 50, Manual = false } }, { "luigi", new SplitInfo { Amount = 50, Manual = false } } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<MemberDashboard>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
            .Add(p => p.memberSlug, memberSlug)
        );

        // Assert
        var headerTitle = cut.Find(".header-title").TextContent;
        headerTitle.Should().Be("Test Trip");

        var profile = cut.Find(".member-profile");
        profile.InnerHtml.Should().NotContain("Test Trip"); // Should not be in profile anymore
        
        var summaryTotal = cut.Find(".summary-total").TextContent; // Totale versato
        summaryTotal.Should().Contain("300");

        var summarySub = cut.Find(".summary-sub").TextContent; // Saldo
        summarySub.Should().Contain("250");

        var transactionRows = cut.FindAll(".transaction-row");
        transactionRows.Should().HaveCount(2);

        // Specifically check that expense row shows 50 (Mario's split), not 100 (total amount)
        var expenseRow = transactionRows.First(r => r.InnerHtml.Contains("expense-icon"));
        var expenseAmount = expenseRow.QuerySelector(".transaction-amount")?.TextContent ?? "";
        expenseAmount.Should().Contain("50");
        expenseAmount.Should().NotContain("-");

        var contribRow = transactionRows.First(r => r.InnerHtml.Contains("contrib-icon"));
        var contribAmount = contribRow.QuerySelector(".transaction-amount")?.TextContent ?? "";
        contribAmount.Should().Contain("300");
        contribAmount.Should().NotContain("+");
    }

    [Fact]
    public void MemberDashboard_ShouldHandleMissingMemberCorrectly()
    {
        // Arrange
        var tripSlug = "test-trip";
        var memberSlug = "missing-user";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency>
            {
                { "EUR", new Currency { Symbol = "€", ExpectedQuotaPerMember = 500 } }
            },
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Avatar = "M" } }
            }
        };

        var transactions = new List<Transaction>
        {
            new Transaction
            {
                Id = "t1",
                Type = "contribution",
                Currency = "EUR",
                Amount = 100,
                Split = new Dictionary<string, SplitInfo> { { "missing-user", new SplitInfo { Amount = 100, Manual = true } } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<MemberDashboard>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
            .Add(p => p.memberSlug, memberSlug)
        );

        // Assert
        var profile = cut.Find(".member-profile");
        profile.ClassName.Should().Contain("missing-member");
        profile.InnerHtml.Should().Contain("missing-user");
        profile.InnerHtml.Should().Contain("❓");
        profile.InnerHtml.Should().NotContain("Partecipante non configurato nel viaggio");

        var summaryTotal = cut.Find(".summary-total").TextContent;
        summaryTotal.Should().Contain("100");
    }

    [Fact]
    public void MemberDashboard_ShouldDisableContributionButtonIfMemberIsMissing()
    {
        // Arrange
        var tripSlug = "test-trip";
        var memberSlug = "missing-user";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency>
            {
                { "EUR", new Currency { Symbol = "€", ExpectedQuotaPerMember = 500 } }
            },
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Avatar = "M" } }
            }
        };

        var transactions = new List<Transaction>
        {
            new Transaction
            {
                Id = "t1",
                Type = "contribution",
                Currency = "EUR",
                Amount = 100,
                Split = new Dictionary<string, SplitInfo> { { "missing-user", new SplitInfo { Amount = 100, Manual = true } } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<MemberDashboard>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
            .Add(p => p.memberSlug, memberSlug)
        );

        // Assert
        var contribBtn = cut.Find(".contribute-btn");
        contribBtn.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void MemberDashboard_ShouldShowEccedenzaWhenOverBudget()
    {
        // Arrange
        var tripSlug = "over-trip";
        var memberSlug = "m";
        var config = new TripConfig
        {
            Id = "1",
            Name = "Over Trip",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { ExpectedQuotaPerMember = 100 } } },
            Members = new Dictionary<string, User> { { "m", new User { Name = "M" } } }
        };
        var transactions = new List<Transaction>
        {
            new Transaction { Type = "contribution", Currency = "EUR", Amount = 150, Split = new Dictionary<string, SplitInfo> { { "m", new SplitInfo { Amount = 150 } } } }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<MemberDashboard>(p => p.Add(x => x.tripSlug, tripSlug).Add(x => x.memberSlug, memberSlug));

        // Assert
        cut.Find(".summary-sub").TextContent.Should().Contain("Eccedenza");
        cut.Find(".progress-bar-fill").ClassName.Should().Contain("warning");
    }
}
