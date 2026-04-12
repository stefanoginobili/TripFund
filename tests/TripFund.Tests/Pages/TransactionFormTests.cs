using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Pages;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;
using FluentAssertions;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Components;
using AngleSharp.Dom;

namespace TripFund.Tests.Pages;

public class TransactionFormTests : BunitContext
{
    private readonly Mock<LocalTripStorageService> _storageMock;
    private readonly Mock<IAlertService> _alertMock;
    private readonly Mock<IEmailService> _emailMock;
    private readonly Mock<INativeDatePickerService> _datePickerMock;
    private readonly Mock<IThumbnailService> _thumbnailMock;

    public TransactionFormTests()
    {
        var itCulture = new System.Globalization.CultureInfo("it-IT");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = itCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = itCulture;

        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        _alertMock = new Mock<IAlertService>();
        _emailMock = new Mock<IEmailService>();
        _datePickerMock = new Mock<INativeDatePickerService>();
        _thumbnailMock = new Mock<IThumbnailService>();
        
        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_alertMock.Object);
        Services.AddSingleton(_emailMock.Object);
        Services.AddSingleton(_datePickerMock.Object);
        Services.AddSingleton(_thumbnailMock.Object);
        Services.AddSingleton(new Mock<IRemoteStorageService>().Object);

        // Mock JS Interop for scrolling (called in OnAfterRender)
        JSInterop.SetupVoid("headerLogic.scrollIntoView", _ => true);
        
        // Mock AppSettings
        var settings = new AppSettings { AuthorName = "Test Author", DeviceId = "test-author" };
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(settings);
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(new LocalTripRegistry());
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

        // Act - Set amount to 30,00
        cut.Find(".amount-input").Change("30,00");

        // Assert - Each of the 3 members should have 10,00
        var splitInputs = cut.FindAll(".split-amount-input");
        splitInputs.Should().HaveCount(3);
        foreach (var input in splitInputs)
        {
            input.GetAttribute("value").Should().Be("10,00");
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
        cut.Find(".amount-input").Change("100");

        // 2. Set Mario to Manual and give him 50
        var marioRow = cut.FindAll(".member-split-row").First(r => r.InnerHtml.Contains("Mario"));
        marioRow.QuerySelector(".split-mode-toggle")!.Click(); 
        
        // Re-find Mario row because of re-render
        marioRow = cut.FindAll(".member-split-row").First(r => r.InnerHtml.Contains("Mario"));
        marioRow.QuerySelector(".split-amount-input")!.Change("50");

        // Assert
        // Luigi and Carlo (Auto) should share the remaining 50 -> 25 each
        var luigiRow = cut.FindAll(".member-split-row").First(r => r.InnerHtml.Contains("Luigi"));
        var carloRow = cut.FindAll(".member-split-row").First(r => r.InnerHtml.Contains("Carlo"));

        luigiRow.QuerySelector(".split-amount-input")!.GetAttribute("value").Should().Be("25,00");
        carloRow.QuerySelector(".split-amount-input")!.GetAttribute("value").Should().Be("25,00");
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
        cut.Find(".amount-input").Change("100");
        
        // Exclude Mario
        var marioRow = cut.FindAll(".member-split-row").First(r => r.InnerHtml.Contains("Mario"));
        marioRow.QuerySelector(".switch")!.Click();

        // Assert
        // Luigi should take the full 100
        var luigiRow = cut.FindAll(".member-split-row").First(r => r.InnerHtml.Contains("Luigi"));
        luigiRow.QuerySelector(".split-amount-input")!.GetAttribute("value").Should().Be("100,00");
        
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

        // Act - Set amount to 10,00
        // 10 / 3 = 3.3333...
        // One should get 3,34, others 3,33
        cut.Find(".amount-input").Change("10,00");

        // Assert
        var splitInputs = cut.FindAll(".split-amount-input");
        var values = splitInputs.Select(i => decimal.Parse(i.GetAttribute("value")!, new System.Globalization.CultureInfo("it-IT"))).ToList();
        
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
        amountInput.GetAttribute("inputmode").Should().Be("numeric");
        amountInput.GetAttribute("placeholder").Should().Be("0");

        // Act - Set amount to 1000
        // 1000 / 3 = 333.333... -> with 0 decimals: 333
        // Remainder: 1000 - (333 * 3) = 1. One should get 334.
        amountInput.Change("1000");

        // Assert
        var splitInputs = cut.FindAll(".split-amount-input");
        var values = splitInputs.Select(i => decimal.Parse(i.GetAttribute("value")!, new System.Globalization.CultureInfo("it-IT"))).ToList();
        
        values.Sum().Should().Be(1000m);
        values.Should().Contain(334m);
        values.Count(v => v == 333m).Should().Be(2);
        
        foreach(var input in splitInputs)
        {
            input.GetAttribute("inputmode").Should().Be("numeric");
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
        cut.Find(".amount-input").Change("100");
        cut.Find("input[placeholder='es. Cena a Buenos Aires']").Change("Test Expense");

        // Set Mario to Manual (Luigi stays Auto)
        var marioRow = cut.FindAll(".member-split-row").First(r => r.InnerHtml.Contains("Mario"));
        marioRow.QuerySelector(".split-mode-toggle")!.Click();
        
        marioRow = cut.FindAll(".member-split-row").First(r => r.InnerHtml.Contains("Mario"));
        marioRow.QuerySelector(".split-amount-input")!.Change("40");

        // Submit
        await cut.Find(".btn-primary-vibe").ClickAsync();

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
        
        // Use custom selector
        cut.Find(".custom-member-selector").Click();
        var marioItem = cut.FindAll(".dropdown-member-item").First(i => i.InnerHtml.Contains("Mario"));
        marioItem.Click();

        // Submit
        await cut.Find(".btn-primary-vibe").ClickAsync();

        // Assert
        savedTransaction.Should().NotBeNull();
        savedTransaction!.Split["mario"].Amount.Should().Be(500);
        savedTransaction.Split["mario"].Manual.Should().BeTrue();
    }

    [Fact]
    public async Task AddContribution_Submit_ShouldUseDefaultDescription()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { { "USD", new Currency { Symbol = "$" } } },
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
        cut.Find(".amount-input").Change("100");
        
        // Select member
        cut.Find(".custom-member-selector").Click();
        var marioItem = cut.FindAll(".dropdown-member-item").First(i => i.InnerHtml.Contains("Mario"));
        marioItem.Click();

        // Submit
        await cut.Find(".btn-primary-vibe").ClickAsync();

        // Assert
        savedTransaction.Should().NotBeNull();
        savedTransaction!.Description.Should().Be("Versamento in cassa");
    }

    [Fact]
    public async Task AddContribution_Submit_ShouldUseManualDescription()
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
        cut.Find(".amount-input").Change("100");
        cut.Find("input[placeholder='es. Versamento iniziale']").Change("Ricarica fondo comune");
        
        // Select member
        cut.Find(".custom-member-selector").Click();
        var marioItem = cut.FindAll(".dropdown-member-item").First(i => i.InnerHtml.Contains("Mario"));
        marioItem.Click();

        // Submit
        await cut.Find(".btn-primary-vibe").ClickAsync();

        // Assert
        savedTransaction.Should().NotBeNull();
        savedTransaction!.Description.Should().Be("Ricarica fondo comune");
    }

    [Fact]
    public void AddContribution_WhenNavigatingFromMemberDashboard_ShouldPresetRemainingQuota()
    {
        // Arrange
        var tripSlug = "test-trip";
        var memberSlug = "mario";
        var currencyCode = "EUR";
        
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> 
            { 
                { currencyCode, new Currency { Symbol = "€", ExpectedQuotaPerMember = 500, Decimals = 2 } } 
            },
            Members = new Dictionary<string, User>
            {
                { memberSlug, new User { Name = "Mario", Avatar = "M" } }
            }
        };

        var transactions = new List<Transaction>
        {
            new Transaction
            {
                Id = "t1",
                Type = "contribution",
                Currency = currencyCode,
                Amount = 200,
                Split = new Dictionary<string, SplitInfo> { { memberSlug, new SplitInfo { Amount = 200 } } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/trip/{tripSlug}/add-contribution?member={memberSlug}&currency={currencyCode}");

        // Act
        var cut = Render<AddContribution>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert
        // Expected: 500 (quota) - 200 (already contributed) = 300
        var amountInput = cut.Find(".amount-input");
        amountInput.GetAttribute("value").Should().Be("300,00");
    }

    [Fact]
    public void AddContribution_WhenQuotaAlreadyReached_ShouldPresetZero()
    {
        // Arrange
        var tripSlug = "test-trip";
        var memberSlug = "mario";
        var currencyCode = "EUR";
        
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> 
            { 
                { currencyCode, new Currency { Symbol = "€", ExpectedQuotaPerMember = 500, Decimals = 2 } } 
            },
            Members = new Dictionary<string, User>
            {
                { memberSlug, new User { Name = "Mario", Avatar = "M" } }
            }
        };

        var transactions = new List<Transaction>
        {
            new Transaction
            {
                Id = "t1",
                Type = "contribution",
                Currency = currencyCode,
                Amount = 600,
                Split = new Dictionary<string, SplitInfo> { { memberSlug, new SplitInfo { Amount = 600 } } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/trip/{tripSlug}/add-contribution?member={memberSlug}&currency={currencyCode}");

        // Act
        var cut = Render<AddContribution>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert
        // Expected: 500 (quota) - 600 (already contributed) = -100 -> should be 0
        var amountInput = cut.Find(".amount-input");
        amountInput.GetAttribute("value").Should().Be("0,00");
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
        attachmentInfoType!.GetProperty("OriginalName")!.SetValue(existing1, "original_old.jpg");
        attachmentInfoType!.GetProperty("CreatedAt")!.SetValue(existing1, DateTime.UtcNow);
        attachmentInfoType!.GetProperty("IsExisting")!.SetValue(existing1, true);
        attachmentsList.Add(existing1);

        // Add 1 existing attachment with conforming name Attachment-07.png
        var existing2 = System.Activator.CreateInstance(attachmentInfoType!);
        attachmentInfoType!.GetProperty("FileName")!.SetValue(existing2, "Attachment-07.png");
        attachmentInfoType!.GetProperty("OriginalName")!.SetValue(existing2, "original_07.png");
        attachmentInfoType!.GetProperty("CreatedAt")!.SetValue(existing2, DateTime.UtcNow);
        attachmentInfoType!.GetProperty("IsExisting")!.SetValue(existing2, true);
        attachmentsList.Add(existing2);

        // Add 2 NEW attachments
        var now1 = DateTime.UtcNow;
        var now2 = now1.AddSeconds(2);
        var nowStr1 = now1.ToString("yyyyMMddTHHmmssfffZ");
        var nowStr2 = now2.ToString("yyyyMMddTHHmmssfffZ");

        var new1 = System.Activator.CreateInstance(attachmentInfoType!);
        attachmentInfoType!.GetProperty("FileName")!.SetValue(new1, "photo.jpg");
        attachmentInfoType!.GetProperty("OriginalName")!.SetValue(new1, "photo.jpg");
        attachmentInfoType!.GetProperty("CreatedAt")!.SetValue(new1, now1);
        attachmentInfoType!.GetProperty("Extension")!.SetValue(new1, ".jpg");
        attachmentInfoType!.GetProperty("IsExisting")!.SetValue(new1, false);
        attachmentInfoType!.GetProperty("Stream")!.SetValue(new1, new MemoryStream(new byte[] { 1 }));
        attachmentsList.Add(new1);

        var new2 = System.Activator.CreateInstance(attachmentInfoType!);
        attachmentInfoType!.GetProperty("FileName")!.SetValue(new2, "doc.pdf");
        attachmentInfoType!.GetProperty("OriginalName")!.SetValue(new2, "doc.pdf");
        attachmentInfoType!.GetProperty("CreatedAt")!.SetValue(new2, now2); // Different timestamp
        attachmentInfoType!.GetProperty("Extension")!.SetValue(new2, ".pdf");
        attachmentInfoType!.GetProperty("IsExisting")!.SetValue(new2, false);
        attachmentInfoType!.GetProperty("Stream")!.SetValue(new2, new MemoryStream(new byte[] { 2 }));
        attachmentsList.Add(new2);

        // Act
        cut.Find(".amount-input").Change("10");
        cut.Find("input[placeholder='es. Cena a Buenos Aires']").Change("Test Attachments");
        await cut.Find(".btn-primary-vibe").ClickAsync();

        // Assert
        savedTransaction.Should().NotBeNull();
        savedTransaction!.Attachments.Should().HaveCount(4);
        savedTransaction.Attachments.Any(a => a.Name == "old_style_guid.jpg").Should().BeTrue();
        savedTransaction.Attachments.Any(a => a.Name == "Attachment-07.png").Should().BeTrue();
        
        // New ones should be ATT_... format
        savedTransaction.Attachments.Any(a => a.Name == $"ATT_{nowStr1}").Should().BeTrue();
        savedTransaction.Attachments.Any(a => a.Name == $"ATT_{nowStr2}").Should().BeTrue();
    }

    [Fact]
    public async Task AddExpense_ChangeDateTime_ShouldUpdateTransactionDate()
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

        // Act
        cut.Find(".amount-input").Change("10");
        cut.Find("input[placeholder='es. Cena a Buenos Aires']").Change("Test DateTime");

        // Change Date to 2026-05-20
        var datePicker = cut.Find("input[type='date']");
        datePicker.Change("2026-05-20");

        // Change Time to 15:30
        var timePicker = cut.Find("input[type='time']");
        timePicker.Change("15:30");

        // Submit
        await cut.Find(".btn-primary-vibe").ClickAsync();

        // Assert
        savedTransaction.Should().NotBeNull();
        var localDate = savedTransaction!.Date.ToLocalTime();
        localDate.Year.Should().Be(2026);
        localDate.Month.Should().Be(5);
        localDate.Day.Should().Be(20);
        localDate.Hour.Should().Be(15);
        localDate.Minute.Should().Be(30);
    }

    [Fact]
    public async Task AddExpense_WithTimezone_ShouldSaveDateTimeOffsetCorrectly()
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

        // Act
        cut.Find(".amount-input").Change("10");
        cut.Find("input[placeholder='es. Cena a Buenos Aires']").Change("Test Timezone");

        // Set Date and Time
        cut.Find("input[type='date']").Change("2024-05-10");
        cut.Find("input[type='time']").Change("10:00");

        // Set Timezone to UTC (safe across platforms) using custom selector
        var tzId = "UTC";
        cut.Find(".custom-tz-selector").Click();
        var utcItem = cut.FindAll(".dropdown-tz-item").First(i => i.QuerySelector(".tz-selector-name")!.TextContent == tzId);
        utcItem.Click();

        // Submit
        await cut.Find(".btn-primary-vibe").ClickAsync();

        // Assert
        savedTransaction.Should().NotBeNull();
        savedTransaction!.Timezone.Should().Be(tzId);
        
        // We expect the date to be 2024-05-10 10:00:00 +00:00
        savedTransaction.Date.Year.Should().Be(2024);
        savedTransaction.Date.Month.Should().Be(5);
        savedTransaction.Date.Day.Should().Be(10);
        savedTransaction.Date.Hour.Should().Be(10);
        savedTransaction.Date.Minute.Should().Be(0);
        savedTransaction.Date.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task AddExpense_WithDifferentTimezone_ShouldNotCrash()
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

        // Act
        cut.Find(".amount-input").Change("10");
        cut.Find("input[placeholder='es. Cena a Buenos Aires']").Change("Test Description");
        
        // Find a timezone with a different offset than local to trigger potential crash
        var localTz = TimeZoneInfo.Local;
        var differentTz = TimeZoneInfo.GetSystemTimeZones()
            .Where(tz => TimeZoneMapper.IsSupported(tz.Id))
            .FirstOrDefault(tz => tz.BaseUtcOffset != localTz.BaseUtcOffset) ?? TimeZoneInfo.Utc;

        var tzId = differentTz.Id;
        cut.Find(".custom-tz-selector").Click();
        var tzItem = cut.FindAll(".dropdown-tz-item").First(i => i.QuerySelector(".tz-selector-name")!.TextContent == tzId);
        tzItem.Click();
        // Submit - This is where it used to crash
        await cut.Find(".btn-primary-vibe").ClickAsync();

        // Assert
        savedTransaction.Should().NotBeNull();
        savedTransaction!.Timezone.Should().Be(tzId);
        savedTransaction.Date.Offset.Should().Be(differentTz.GetUtcOffset(savedTransaction.Date.DateTime));
    }

    [Fact]
    public void EditExpense_ShouldDeleteTransaction()
    {
        // Arrange
        var tripSlug = "test-trip";
        var transactionId = "trans-123";
        var nav = Services.GetRequiredService<NavigationManager>();

        var config = new TripConfig { Id = "1", Name = "Test Trip", Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€" } } } };
        var transaction = new Transaction { Id = transactionId, Type = "expense", Amount = 100, Currency = "EUR", Description = "Test" };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetLatestTransactionVersionWithMetadataAsync(tripSlug, transactionId))
            .ReturnsAsync(new LocalTripStorageService.TransactionVersionInfo { Transaction = transaction });
        _alertMock.Setup(a => a.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        nav.NavigateTo($"/trip/{tripSlug}/add-expense?edit={transactionId}");
        var cut = Render<AddExpense>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
        );

        // Act - Open menu
        var menuBtn = cut.Find("header .dropdown button.icon-btn");
        menuBtn.Click();

        // Click delete
        var deleteBtn = cut.Find(".dropdown-item-vibe.text-danger");
        deleteBtn.Click();

        // Assert
        _alertMock.Verify(a => a.ConfirmAsync("Elimina Spesa", It.IsAny<string>(), "Elimina", "Annulla"), Times.Once);
        _storageMock.Verify(s => s.SaveTransactionAsync(tripSlug, transaction, "test-author", true, It.IsAny<Dictionary<string, byte[]>>()), Times.Once);
        nav.Uri.Should().Contain($"/trip/{tripSlug}?currency=EUR");
    }
}
