using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Pages;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using System.Collections.Generic;
using System.Linq;
using AngleSharp.Dom;

namespace TripFund.Tests.Pages;

public class DashboardTests : BunitContext
{
    private readonly Mock<LocalTripStorageService> _storageMock;

    public DashboardTests()
    {
        var itCulture = new System.Globalization.CultureInfo("it-IT");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = itCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = itCulture;

        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(new LocalTripRegistry());
        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(new Mock<IEmailService>().Object);
        Services.AddSingleton(new Mock<IAlertService>().Object);
        Services.AddSingleton(new Mock<IThumbnailService>().Object);
        Services.AddSingleton(new Mock<IRemoteStorageService>().Object);
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
        summarySub.Should().Contain("300"); // Quota versata

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

    [Theory]
    [InlineData(0, "Sincronizzato meno di un minuto fa")]
    [InlineData(1, "Sincronizzato un minuto fa")]
    [InlineData(5, "Sincronizzato 5 minuti fa")]
    [InlineData(60, "Sincronizzato più di un'ora fa")]
    [InlineData(120, "Sincronizzato più di 2 ore fa")]
    public void TripDashboard_ShouldFormatSyncTimeCorrectly(int minutesAgo, string expectedText)
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig { Id = "1", Name = "Test Trip", Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } } };
        var lastSynchronized = DateTime.Now.AddMinutes(-minutesAgo);
        var registry = new LocalTripRegistry
        {
            Trips = new Dictionary<string, TripRegistryEntry>
            {
                { tripSlug, new TripRegistryEntry { RemoteStorage = new RemoteStorageConfig { Provider = "google-drive", LastSynchronized = lastSynchronized } } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(new List<Transaction>());
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(registry);

        // Act
        var cut = Render<TripDashboard>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert
        var syncText = cut.Find(".sync-text").TextContent;
        syncText.Should().Be(expectedText);
    }

    [Fact]
    public void TripDashboard_ShouldShowMaiSincronizzatoWhenLastSynchronizedIsNull()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig { Id = "1", Name = "Test Trip", Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } } };
        var registry = new LocalTripRegistry
        {
            Trips = new Dictionary<string, TripRegistryEntry>
            {
                { tripSlug, new TripRegistryEntry { RemoteStorage = new RemoteStorageConfig { Provider = "google-drive", LastSynchronized = null } } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(new List<Transaction>());
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(registry);

        // Act
        var cut = Render<TripDashboard>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert
        var syncText = cut.Find(".sync-text").TextContent;
        syncText.Should().Be("Mai sincronizzato");
    }

    [Fact]
    public async Task TripDashboard_ShouldTriggerSyncWhenButtonClicked()
    {
        // Arrange
        var tripSlug = "test-trip";
        var remoteStorageMock = new Mock<IRemoteStorageService>();
        Services.AddSingleton(remoteStorageMock.Object);

        var config = new TripConfig { Id = "1", Name = "Test Trip", Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } } };
        var registry = new LocalTripRegistry
        {
            Trips = new Dictionary<string, TripRegistryEntry>
            {
                { tripSlug, new TripRegistryEntry { RemoteStorage = new RemoteStorageConfig { Provider = "google-drive" } } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(new List<Transaction>());
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(registry);

        var cut = Render<TripDashboard>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act
        var syncBtn = cut.Find(".sync-now-btn");
        await syncBtn.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        remoteStorageMock.Verify(s => s.SynchronizeAsync(tripSlug), Times.Once);
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
        cut.Find(".summary-label").TextContent.Should().Be("Saldo");
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

        // JPY should show 50.001 (rounded by N0 in it-IT, where . is thousands separator)
        var summarySub = cut.Find(".summary-sub").TextContent; // Quota versata

        // We check for absence of decimal part
        summarySub.Should().NotContain(",75");

        // In it-IT, N0 for 50001 is "50.001"
        summarySub.Should().Contain("50.001");

        var txRow = cut.Find(".transaction-row");
        txRow.InnerHtml.Should().Contain("¥ 50.001");
        // We ensure it doesn't have decimal digits
        txRow.InnerHtml.Should().NotMatchRegex(@"50\.001,\d+");
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
    public void TripDashboard_ShouldShowThreeDotMenuWithEditAction()
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

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(new List<Transaction>());

        // Act
        var cut = Render<TripDashboard>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert - Three-dot button should be present
        AngleSharp.Dom.IElement menuBtn = cut.FindAll(".header-actions .icon-btn").First();
        menuBtn.InnerHtml.Should().Contain("<circle"); // SVG for three dots

        // Menu should be closed initially
        cut.FindAll(".dropdown-menu-vibe").Should().BeEmpty();

        // Act - Open menu
        menuBtn.Click();

        // Assert - Menu should be open
        AngleSharp.Dom.IElement dropdown = cut.FindAll(".dropdown-menu-vibe").First();
        AngleSharp.Dom.IElement? editBtn = dropdown.QuerySelector(".dropdown-item-vibe");
        editBtn.Should().NotBeNull();
        editBtn!.TextContent.Should().Contain("Modifica");
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
        
        var summaryTotal = cut.Find(".summary-sub").TextContent; // Quota versata
        summaryTotal.Should().Contain("300");

        var summarySub = cut.Find(".summary-total").TextContent; // Saldo
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
        cut.Find(".summary-sub").TextContent.Should().Contain("Quota versata");
        cut.Find(".summary-label").TextContent.Should().Contain("Saldo");
        cut.Find(".progress-bar-fill").ClassName.Should().Contain("warning");
    }

    [Fact]
    public void MemberDashboard_ShouldShowThreeDotMenuWithSummaryEmailAction()
    {
        // Arrange
        var tripSlug = "test-trip";
        var memberSlug = "mario";
        var emailServiceMock = new Mock<IEmailService>();
        var alertServiceMock = new Mock<IAlertService>();
        Services.AddSingleton(emailServiceMock.Object);
        Services.AddSingleton(alertServiceMock.Object);

        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } },
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario", Email = "mario@example.com" } } }
        };

        var transactions = new List<Transaction>
        {
            new Transaction
            {
                Id = "t1",
                Type = "contribution",
                Currency = "EUR",
                Amount = 100,
                Split = new Dictionary<string, SplitInfo> { { "mario", new SplitInfo { Amount = 100, Manual = true } } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<MemberDashboard>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
            .Add(p => p.memberSlug, memberSlug)
        );

        // Assert - Three-dot button should be present
        var menuBtn = cut.Find(".header-actions .icon-btn");
        menuBtn.InnerHtml.Should().Contain("<circle");

        // Menu should be closed initially
        cut.FindAll(".dropdown-menu-vibe").Should().BeEmpty();

        // Act - Open menu
        menuBtn.Click();

        // Assert - Menu should be open
        var dropdown = cut.Find(".dropdown-menu-vibe");
        var summaryBtn = dropdown.QuerySelector(".dropdown-item-vibe");
        summaryBtn.Should().NotBeNull();
        summaryBtn!.TextContent.Should().Contain("Riepilogo Versamenti");
        summaryBtn.HasAttribute("disabled").Should().BeFalse();

        // Act - Click summary button
        summaryBtn.Click();

        // Assert - Email service should be called
        emailServiceMock.Verify(e => e.SendEmailAsync(
            It.Is<string>(s => s.Contains("Riepilogo versamenti")),
            It.IsAny<string>(),
            It.Is<string[]>(a => a.Contains("mario@example.com"))
        ), Times.Once);
    }

    [Fact]
    public void MemberDashboard_SummaryEmailAction_ShouldBeDisabledIfNoContributions()
    {
        // Arrange
        var tripSlug = "test-trip";
        var memberSlug = "mario";
        Services.AddSingleton(new Mock<IEmailService>().Object);
        Services.AddSingleton(new Mock<IAlertService>().Object);

        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } },
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario", Email = "mario@example.com" } } }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(new List<Transaction>());

        // Act
        var cut = Render<MemberDashboard>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
            .Add(p => p.memberSlug, memberSlug)
        );

        // Act - Open menu
        cut.Find(".header-actions .icon-btn").Click();

        // Assert - Button should be disabled
        var summaryBtn = cut.Find(".dropdown-item-vibe");
        summaryBtn.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void MemberDashboard_SummaryEmailAction_ShouldBeDisabledIfMemberHasNoEmail()
    {
        // Arrange
        var tripSlug = "test-trip";
        var memberSlug = "mario";
        Services.AddSingleton(new Mock<IEmailService>().Object);
        Services.AddSingleton(new Mock<IAlertService>().Object);

        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } },
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario", Email = "" } } }
        };

        var transactions = new List<Transaction>
        {
            new Transaction
            {
                Id = "t1",
                Type = "contribution",
                Currency = "EUR",
                Amount = 100,
                Split = new Dictionary<string, SplitInfo> { { "mario", new SplitInfo { Amount = 100, Manual = true } } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<MemberDashboard>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
            .Add(p => p.memberSlug, memberSlug)
        );

        // Act - Open menu
        cut.Find(".header-actions .icon-btn").Click();

        // Assert - Button should be disabled
        var summaryBtn = cut.Find(".dropdown-item-vibe");
        summaryBtn.HasAttribute("disabled").Should().BeTrue();
    }
}
