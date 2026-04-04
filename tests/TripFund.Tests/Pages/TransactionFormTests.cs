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
            input.GetAttribute("value").Should().Be("10.00");
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

        luigiRow.QuerySelector(".split-amount-input")!.GetAttribute("value").Should().Be("25.00");
        carloRow.QuerySelector(".split-amount-input")!.GetAttribute("value").Should().Be("25.00");
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
        luigiRow.QuerySelector(".split-amount-input")!.GetAttribute("value").Should().Be("100.00");
        
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

    [Fact]
    public void AddExpense_ZeroDecimals_ShouldRoundAndSplitCorrectly()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { { "JPY", new Currency { Symbol = "¥", Decimals = 0 } } },
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Avatar = "M" } },
                { "luigi", new User { Name = "Luigi", Avatar = "L" } },
                { "carlo", new User { Name = "Carlo", Avatar = "C" } }
            }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);

        var cut = Render<AddExpense>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert UI Attributes
        var amountInput = cut.Find(".amount-input");
        amountInput.GetAttribute("step").Should().Be("1");
        amountInput.GetAttribute("placeholder").Should().Be("0");

        // Act - Set amount to 1000
        // 1000 / 3 = 333.333... -> with 0 decimals: 333
        // Remainder: 1000 - (333 * 3) = 1. One should get 334.
        amountInput.Input("1000");

        // Assert
        var splitInputs = cut.FindAll(".split-amount-input");
        var values = splitInputs.Select(i => decimal.Parse(i.GetAttribute("value")!, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        
        values.Sum().Should().Be(1000m);
        values.Should().Contain(334m);
        values.Count(v => v == 333m).Should().Be(2);
        
        foreach(var input in splitInputs)
        {
            input.GetAttribute("step").Should().Be("1");
        }
    }

    [Fact]
    public async Task AddExpense_Submit_ShouldSaveManualFlagCorrectly()
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
        
        Transaction? savedTransaction = null;
        _storageMock.Setup(s => s.SaveTransactionAsync(tripSlug, It.IsAny<Transaction>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Dictionary<string, byte[]>>()))
            .Callback<string, Transaction, string, bool, Dictionary<string, byte[]>>((s, t, d, b, a) => savedTransaction = t)
            .Returns(Task.CompletedTask);

        var cut = Render<AddExpense>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act
        cut.Find(".amount-input").Input("100");
        cut.Find("input[placeholder='es. Cena a Buenos Aires']").Change("Test Expense");

        // Set Mario to Manual (Luigi stays Auto)
        var marioRow = cut.FindAll(".member-split-row").First(r => r.InnerHtml.Contains("Mario"));
        marioRow.QuerySelector(".split-mode-toggle")!.Click();
        
        marioRow = cut.FindAll(".member-split-row").First(r => r.InnerHtml.Contains("Mario"));
        marioRow.QuerySelector(".split-amount-input")!.Input("40");

        // Submit
        await cut.Find(".submit-btn").ClickAsync();

        // Assert
        savedTransaction.Should().NotBeNull();
        savedTransaction!.Split["mario"].Amount.Should().Be(40);
        savedTransaction.Split["mario"].Manual.Should().BeTrue();
        
        savedTransaction.Split["luigi"].Amount.Should().Be(60);
        savedTransaction.Split["luigi"].Manual.Should().BeFalse();
    }

    [Fact]
    public async Task AddContribution_Submit_ShouldAlwaysSaveAsManual()
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
                { "mario", new User { Name = "Mario", Avatar = "M" } }
            }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        
        Transaction? savedTransaction = null;
        _storageMock.Setup(s => s.SaveTransactionAsync(tripSlug, It.IsAny<Transaction>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Dictionary<string, byte[]>>()))
            .Callback<string, Transaction, string, bool, Dictionary<string, byte[]>>((s, t, d, b, a) => savedTransaction = t)
            .Returns(Task.CompletedTask);

        var cut = Render<AddContribution>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act
        cut.Find(".amount-input").Change("500");
        cut.Find("select.form-control").Change("mario");

        // Submit
        await cut.Find(".submit-btn").ClickAsync();

        // Assert
        savedTransaction.Should().NotBeNull();
        savedTransaction!.Split["mario"].Amount.Should().Be(500);
        savedTransaction.Split["mario"].Manual.Should().BeTrue();
    }

    [Fact]
    public async Task AddExpense_Submit_ShouldUseNumberedAttachmentNaming()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } },
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario" } } }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        
        Transaction? savedTransaction = null;
        _storageMock.Setup(s => s.SaveTransactionAsync(tripSlug, It.IsAny<Transaction>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Dictionary<string, byte[]>>()))
            .Callback<string, Transaction, string, bool, Dictionary<string, byte[]>>((s, t, d, b, a) => savedTransaction = t)
            .Returns(Task.CompletedTask);

        var cut = Render<AddExpense>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Use reflection to add attachments to the private list
        var attachmentsField = typeof(AddExpense).GetField("attachments", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var attachmentsList = (System.Collections.IList)attachmentsField!.GetValue(cut.Instance)!;

        // Add 1 existing attachment with a non-conforming name
        var attachmentInfoType = typeof(AddExpense).GetNestedType("AttachmentInfo", System.Reflection.BindingFlags.NonPublic);
        
        var existing1 = System.Activator.CreateInstance(attachmentInfoType!);
        attachmentInfoType!.GetProperty("FileName")!.SetValue(existing1, "old_style_guid.jpg");
        attachmentInfoType!.GetProperty("IsExisting")!.SetValue(existing1, true);
        attachmentsList.Add(existing1);

        // Add 1 existing attachment with conforming name Attachment-07.png
        var existing2 = System.Activator.CreateInstance(attachmentInfoType!);
        attachmentInfoType!.GetProperty("FileName")!.SetValue(existing2, "Attachment-07.png");
        attachmentInfoType!.GetProperty("IsExisting")!.SetValue(existing2, true);
        attachmentsList.Add(existing2);

        // Add 2 NEW attachments
        var new1 = System.Activator.CreateInstance(attachmentInfoType!);
        attachmentInfoType!.GetProperty("FileName")!.SetValue(new1, "photo.jpg");
        attachmentInfoType!.GetProperty("Extension")!.SetValue(new1, ".jpg");
        attachmentInfoType!.GetProperty("IsExisting")!.SetValue(new1, false);
        attachmentInfoType!.GetProperty("Stream")!.SetValue(new1, new MemoryStream(new byte[] { 1 }));
        attachmentsList.Add(new1);

        var new2 = System.Activator.CreateInstance(attachmentInfoType!);
        attachmentInfoType!.GetProperty("FileName")!.SetValue(new2, "doc.pdf");
        attachmentInfoType!.GetProperty("Extension")!.SetValue(new2, ".pdf");
        attachmentInfoType!.GetProperty("IsExisting")!.SetValue(new2, false);
        attachmentInfoType!.GetProperty("Stream")!.SetValue(new2, new MemoryStream(new byte[] { 2 }));
        attachmentsList.Add(new2);

        // Act
        cut.Find(".amount-input").Input("10");
        cut.Find("input[placeholder='es. Cena a Buenos Aires']").Change("Test Attachments");
        await cut.Find(".submit-btn").ClickAsync();

        // Assert
        savedTransaction.Should().NotBeNull();
        savedTransaction!.Attachments.Should().HaveCount(4);
        savedTransaction.Attachments.Should().Contain("old_style_guid.jpg");
        savedTransaction.Attachments.Should().Contain("Attachment-07.png");
        
        // New ones should be 08 and 09
        savedTransaction.Attachments.Should().Contain("Attachment-08.jpg");
        savedTransaction.Attachments.Should().Contain("Attachment-09.pdf");
    }
}
