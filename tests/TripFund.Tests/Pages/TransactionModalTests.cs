using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Common;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Components;

namespace TripFund.Tests.Pages;

public class TransactionModalTests : BunitContext
{
    private readonly Mock<LocalTripStorageService> _storageMock;
    private readonly Mock<IAlertService> _alertMock;
    private readonly Mock<IThumbnailService> _thumbnailMock;

    public TransactionModalTests()
    {
        var itCulture = new System.Globalization.CultureInfo("it-IT");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = itCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = itCulture;

        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        _alertMock = new Mock<IAlertService>();
        _thumbnailMock = new Mock<IThumbnailService>();
        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_alertMock.Object);
        Services.AddSingleton(_thumbnailMock.Object);
    }

    [Fact]
    public void TransactionModal_ShouldDisplayMissingMembersWithGrayscaleAndQuestionMark()
    {
        // Arrange
        var tripSlug = "test-trip";
        
        var config = new TripConfig 
        { 
            Id = "1", 
            Name = "Test Trip",
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Avatar = "M" } }
            },
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } }
        };
        var transaction = new Transaction 
        { 
            Id = "trans-123", 
            Amount = 100, 
            Currency = "EUR", 
            Description = "Test",
            Split = new Dictionary<string, SplitInfo>
            {
                { "mario", new SplitInfo { Amount = 60 } },
                { "missing-user", new SplitInfo { Amount = 40 } }
            }
        };

        var cut = Render<TransactionModal>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Transaction, transaction)
            .Add(p => p.Config, config)
            .Add(p => p.TripSlug, tripSlug)
        );

        // Assert
        var splitRows = cut.FindAll(".split-row");
        splitRows.Should().HaveCount(2);

        var marioRow = splitRows.First(r => r.InnerHtml.Contains("Mario"));
        marioRow.ClassName.Should().NotContain("missing-member");
        marioRow.InnerHtml.Should().Contain("M");

        var missingRow = splitRows.First(r => r.InnerHtml.Contains("missing-user"));
        missingRow.ClassName.Should().Contain("missing-member");
        missingRow.InnerHtml.Should().Contain("❓");
    }

    [Fact]
    public void TransactionModal_ShouldShowCurrencyCodeInsteadOfSymbol()
    {
        // Arrange
        var tripSlug = "test-trip";
        
        var config = new TripConfig 
        { 
            Id = "1", 
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€", Decimals = 2 } } }
        };
        var transaction = new Transaction 
        { 
            Id = "trans-123", 
            Amount = 123.45m, 
            Currency = "EUR", 
            Description = "Dinner",
            Split = new Dictionary<string, SplitInfo>()
        };

        var cut = Render<TransactionModal>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Transaction, transaction)
            .Add(p => p.Config, config)
            .Add(p => p.TripSlug, tripSlug)
        );

        // Assert
        var amountTitle = cut.Find(".amount-title");
        amountTitle.TextContent.Trim().Should().Be("EUR 123,45");
        amountTitle.TextContent.Should().NotContain("€");
    }

    [Fact]
    public async Task TransactionModal_ShouldDisableEditIfMemberIsMissing()
    {
        // Arrange
        var tripSlug = "test-trip";
        
        var config = new TripConfig 
        { 
            Id = "1", 
            Name = "Test Trip",
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Avatar = "M" } }
            },
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } }
        };
        var transaction = new Transaction 
        { 
            Id = "trans-123", 
            Amount = 100, 
            Currency = "EUR", 
            Description = "Test",
            Split = new Dictionary<string, SplitInfo>
            {
                { "mario", new SplitInfo { Amount = 60 } },
                { "missing-user", new SplitInfo { Amount = 40 } }
            }
        };

        var cut = Render<TransactionModal>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Transaction, transaction)
            .Add(p => p.Config, config)
            .Add(p => p.TripSlug, tripSlug)
        );

        // Assert
        var editBtn = cut.Find(".edit-action-link");
        editBtn.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TransactionModal_ShouldShowAttachmentsInGrid()
    {
        // Arrange
        var tripSlug = "test-trip";
        
        var config = new TripConfig { Id = "1", Name = "Test Trip" };
        var now = new DateTime(2026, 4, 10, 10, 30, 0, DateTimeKind.Utc);
        var transaction = new Transaction 
        { 
            Id = "trans-123", 
            Amount = 100, 
            Currency = "EUR", 
            Description = "Test",
            Timezone = "UTC",
            Attachments = new List<TransactionAttachment> 
            { 
                new TransactionAttachment { Name = "receipt1.jpg", OriginalName = "my_receipt.jpg", CreatedAt = now },
                new TransactionAttachment { Name = "doc1.pdf", OriginalName = "my_doc.pdf", CreatedAt = now.AddMinutes(5) }
            },
            Split = new Dictionary<string, SplitInfo>()
        };

        _storageMock.Setup(s => s.GetAttachmentPath(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string ts, string tid, string fn) => $"fake/path/{fn}");
        
        _thumbnailMock.Setup(t => t.GetThumbnailBase64Async(It.Is<string>(s => s.Contains("receipt1.jpg"))))
            .ReturnsAsync("data:image/jpeg;base64,fake");

        var cut = Render<TransactionModal>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Transaction, transaction)
            .Add(p => p.Config, config)
            .Add(p => p.TripSlug, tripSlug)
        );

        // Assert
        var attachmentsGrid = cut.Find(".attachments-grid");
        attachmentsGrid.Should().NotBeNull();
        
        var previewWrappers = cut.FindAll(".attachment-preview-wrapper");
        previewWrappers.Should().HaveCount(2);

        // Display timestamp (localized to UTC as per transaction.Timezone)
        cut.FindAll(".attachment-name")[0].TextContent.Should().Be("10/04/2026 10:30");
        cut.FindAll(".attachment-name")[1].TextContent.Should().Be("10/04/2026 10:35");
        
        // Full name should still be in the HTML as alt text for images
        var img = cut.Find("img");
        img.GetAttribute("alt").Should().Be("receipt1.jpg");
        
        // Check for PDF extension in placeholder
        var pdfWrapper = previewWrappers.First(w => w.QuerySelector(".attachment-name")!.TextContent == "10/04/2026 10:35");
        pdfWrapper.QuerySelector(".file-ext")!.TextContent.Should().Be("PDF");
    }

    [Fact]
    public void TransactionModal_ShouldDisplayDateInStoredTimezone()
    {
        // Arrange
        var tripSlug = "test-trip";
        
        var config = new TripConfig 
        { 
            Id = "1", 
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } }
        };
        
        // 10:00 AM UTC
        var date = new DateTimeOffset(2024, 5, 10, 10, 0, 0, TimeSpan.Zero);
        var transaction = new Transaction 
        { 
            Id = "trans-123", 
            Amount = 100, 
            Currency = "EUR", 
            Date = date,
            Timezone = TimeZoneInfo.Utc.Id,
            Split = new Dictionary<string, SplitInfo>()
        };

        var cut = Render<TransactionModal>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Transaction, transaction)
            .Add(p => p.Config, config)
            .Add(p => p.TripSlug, tripSlug)
        );

        // Assert
        var metaTags = cut.FindAll(".transaction-meta");
        // The one with the date should be "10/05/2024 10:00 (UTC)"
        metaTags.Any(m => m.TextContent.Contains("10/05/2024 10:00 (UTC)")).Should().BeTrue();
    }
}
