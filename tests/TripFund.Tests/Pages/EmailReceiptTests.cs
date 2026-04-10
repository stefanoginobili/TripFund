using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Pages;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;

namespace TripFund.Tests.Pages;

public class EmailReceiptTests : BunitContext
{
    private readonly Mock<LocalTripStorageService> _storageMock;
    private readonly Mock<IEmailService> _emailMock;
    private readonly Mock<IAlertService> _alertMock;

    public EmailReceiptTests()
    {
        var itCulture = new System.Globalization.CultureInfo("it-IT");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = itCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = itCulture;

        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        _emailMock = new Mock<IEmailService>();
        _alertMock = new Mock<IAlertService>();

        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_emailMock.Object);
        Services.AddSingleton(_alertMock.Object);
        Services.AddSingleton(new Mock<INativeDatePickerService>().Object);
        Services.AddSingleton(new Mock<IRemoteStorageService>().Object);

        // Mock JS Interop for scrolling (called in OnAfterRender)
        JSInterop.SetupVoid("headerLogic.scrollIntoView", _ => true);

        // Mock AppSettings
        var settings = new AppSettings { AuthorName = "Test Author", DeviceId = "test-author" };
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(settings);
        _storageMock.Setup(s => s.GetTripRegistryAsync()).ReturnsAsync(new LocalTripRegistry());
    }

    [Fact]
    public async Task AddContribution_ShouldPromptAndSendEmail_IfAccepted()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency> { 
                { "EUR", new Currency { Symbol = "€", Name = "Euro", Decimals = 2, ExpectedQuotaPerMember = 1000 } } 
            },
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Email = "mario@example.com", Avatar = "M" } }
            }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        
        var transactions = new List<Transaction>();
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);
        
        _storageMock.Setup(s => s.SaveTransactionAsync(tripSlug, It.IsAny<Transaction>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Dictionary<string, byte[]>>()))
            .Callback<string, Transaction, string, bool, Dictionary<string, byte[]>>((s, t, d, b, a) => transactions.Add(t))
            .Returns(Task.CompletedTask);
        
        // Mock alert confirmation to return TRUE (Sì)
        _alertMock.Setup(a => a.ConfirmAsync("Invia ricevuta", It.IsAny<string>(), "Sì", "No"))
            .ReturnsAsync(true);

        var cut = Render<AddContribution>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act
        cut.Find(".amount-input").Change("500");
        
        // Select member
        cut.Find(".custom-member-selector").Click();
        var marioItem = cut.FindAll(".dropdown-member-item").First(i => i.InnerHtml.Contains("Mario"));
        marioItem.Click();

        // Submit
        await cut.Find(".btn-primary-vibe").ClickAsync();

        // Assert
        // 1. Verify Alert was shown
        _alertMock.Verify(a => a.ConfirmAsync("Invia ricevuta", It.Is<string>(s => s.Contains("Mario")), "Sì", "No"), Times.Once);
        
        // 2. Verify Email was sent
        _emailMock.Verify(e => e.SendEmailAsync(
            It.Is<string>(s => s.Contains("Riepilogo versamenti")),
            It.Is<string>(body => body.Contains("500,00") && body.Contains("Test Trip") && body.Contains("DETTAGLIO VERSAMENTI")),
            It.Is<IEnumerable<string>>(rec => rec.Contains("mario@example.com"))
        ), Times.Once);
    }

    [Fact]
    public async Task AddContribution_ShouldNotSendEmail_IfDeclined()
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
                { "mario", new User { Name = "Mario", Email = "mario@example.com", Avatar = "M" } }
            }
        };
        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(new List<Transaction>());
        
        // Mock alert confirmation to return FALSE (No)
        _alertMock.Setup(a => a.ConfirmAsync("Invia ricevuta", It.IsAny<string>(), "Sì", "No"))
            .ReturnsAsync(false);

        var cut = Render<AddContribution>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Act
        cut.Find(".amount-input").Change("500");
        
        // Select member
        cut.Find(".custom-member-selector").Click();
        var marioItem = cut.FindAll(".dropdown-member-item").First(i => i.InnerHtml.Contains("Mario"));
        marioItem.Click();

        // Submit
        await cut.Find(".btn-primary-vibe").ClickAsync();

        // Assert
        // 1. Verify Alert was shown
        _alertMock.Verify(a => a.ConfirmAsync("Invia ricevuta", It.IsAny<string>(), "Sì", "No"), Times.Once);

        // 2. Verify Email was NEVER sent
        _emailMock.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }
}
