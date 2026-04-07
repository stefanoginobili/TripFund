using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Pages;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Components;

namespace TripFund.Tests.Pages;

public class TransactionDetailTests : BunitContext
{
    private readonly Mock<LocalTripStorageService> _storageMock;
    private readonly Mock<IAlertService> _alertMock;
    private readonly Mock<IThumbnailService> _thumbnailMock;

    public TransactionDetailTests()
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
    public void TransactionDetail_ShouldDisplayMissingMembersWithGrayscaleAndQuestionMark()
    {
        // Arrange
        var tripSlug = "test-trip";
        var transactionId = "trans-123";
        
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
            Id = transactionId, 
            Amount = 100, 
            Currency = "EUR", 
            Description = "Test",
            Split = new Dictionary<string, SplitInfo>
            {
                { "mario", new SplitInfo { Amount = 60 } },
                { "missing-user", new SplitInfo { Amount = 40 } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetLatestTransactionVersionAsync(tripSlug, transactionId)).ReturnsAsync(transaction);

        var cut = Render<TransactionDetail>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
            .Add(p => p.transactionId, transactionId)
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
    public void TransactionDetail_ShouldShowCurrencyCodeInsteadOfSymbol()
    {
        // Arrange
        var tripSlug = "test-trip";
        var transactionId = "trans-123";
        
        var config = new TripConfig 
        { 
            Id = "1", 
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€", Decimals = 2 } } }
        };
        var transaction = new Transaction 
        { 
            Id = transactionId, 
            Amount = 123.45m, 
            Currency = "EUR", 
            Description = "Dinner",
            Split = new Dictionary<string, SplitInfo>()
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetLatestTransactionVersionAsync(tripSlug, transactionId)).ReturnsAsync(transaction);

        var cut = Render<TransactionDetail>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
            .Add(p => p.transactionId, transactionId)
        );

        // Assert
        var amountTitle = cut.Find(".amount-title");
        amountTitle.TextContent.Trim().Should().Be("EUR 123,45");
        amountTitle.TextContent.Should().NotContain("€");
    }

    [Fact]
    public async Task TransactionDetail_ShouldDisableEditIfMemberIsMissing()
    {
        // Arrange
        var tripSlug = "test-trip";
        var transactionId = "trans-123";
        
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
            Id = transactionId, 
            Amount = 100, 
            Currency = "EUR", 
            Description = "Test",
            Split = new Dictionary<string, SplitInfo>
            {
                { "mario", new SplitInfo { Amount = 60 } },
                { "missing-user", new SplitInfo { Amount = 40 } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetLatestTransactionVersionAsync(tripSlug, transactionId)).ReturnsAsync(transaction);

        var cut = Render<TransactionDetail>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
            .Add(p => p.transactionId, transactionId)
        );

        // Act
        // Open menu
        var menuBtn = cut.Find("header .dropdown button.icon-btn");
        await cut.InvokeAsync(() => menuBtn.Click());

        // Assert
        var editBtn = cut.FindAll(".dropdown-item").First(b => b.InnerHtml.Contains("Modifica"));
        editBtn.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TransactionDetail_ShouldShowAttachmentsInGrid()
    {
        // Arrange
        var tripSlug = "test-trip";
        var transactionId = "trans-123";
        
        var config = new TripConfig { Id = "1", Name = "Test Trip" };
        var transaction = new Transaction 
        { 
            Id = transactionId, 
            Amount = 100, 
            Currency = "EUR", 
            Description = "Test",
            Attachments = new List<string> { "receipt1.jpg", "doc1.pdf" },
            Split = new Dictionary<string, SplitInfo>()
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetLatestTransactionVersionAsync(tripSlug, transactionId)).ReturnsAsync(transaction);
        _storageMock.Setup(s => s.GetAttachmentPath(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string ts, string tid, string fn) => $"fake/path/{fn}");
        
        _thumbnailMock.Setup(t => t.GetThumbnailBase64Async(It.Is<string>(s => s.Contains("receipt1.jpg"))))
            .ReturnsAsync("data:image/jpeg;base64,fake");

        var cut = Render<TransactionDetail>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
            .Add(p => p.transactionId, transactionId)
        );

        // Assert
        var attachmentsGrid = cut.Find(".attachments-grid");
        attachmentsGrid.Should().NotBeNull();
        
        var previewWrappers = cut.FindAll(".attachment-preview-wrapper");
        previewWrappers.Should().HaveCount(2);

        // Name without extension in the caption
        cut.FindAll(".attachment-name")[0].TextContent.Should().Be("receipt1");
        cut.FindAll(".attachment-name")[1].TextContent.Should().Be("doc1");
        
        // Full name should still be in the HTML as alt text for images
        var img = cut.Find("img");
        img.GetAttribute("alt").Should().Be("receipt1.jpg");
        
        // Check for PDF extension in placeholder
        var pdfWrapper = previewWrappers.First(w => w.QuerySelector(".attachment-name")!.TextContent == "doc1");
        pdfWrapper.QuerySelector(".file-ext")!.TextContent.Should().Be("PDF");
    }
}
