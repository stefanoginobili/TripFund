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

    public TransactionDetailTests()
    {
        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        _alertMock = new Mock<IAlertService>();
        Services.AddSingleton(_storageMock.Object);
        Services.AddSingleton(_alertMock.Object);
    }

    [Fact]
    public async Task DeleteTransaction_ShouldCallServiceAndNavigate()
    {
        // Arrange
        var nav = Services.GetRequiredService<NavigationManager>();
        var tripSlug = "test-trip";
        var transactionId = "trans-123";
        
        var config = new TripConfig { Id = "1", Name = "Test Trip" };
        var transaction = new Transaction { Id = transactionId, Amount = 100, Currency = "EUR", Description = "Test" };
        var settings = new AppSettings { AuthorName = "Mario", DeviceId = "mario" };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetLatestTransactionVersionAsync(tripSlug, transactionId)).ReturnsAsync(transaction);
        _storageMock.Setup(s => s.GetAppSettingsAsync()).ReturnsAsync(settings);
        _alertMock.Setup(a => a.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var cut = Render<TransactionDetail>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
            .Add(p => p.transactionId, transactionId)
        );

        // Act
        // Open menu
        var menuBtn = cut.Find("header .dropdown button.icon-btn");
        await cut.InvokeAsync(() => menuBtn.Click());

        // Click delete
        var deleteBtn = cut.Find(".dropdown-item.text-danger");
        await cut.InvokeAsync(() => deleteBtn.Click());

        // Assert
        _alertMock.Verify(a => a.ConfirmAsync("Elimina Transazione", It.IsAny<string>(), "Elimina", "Annulla"), Times.Once);
        _storageMock.Verify(s => s.SaveTransactionAsync(tripSlug, transaction, "mario", true, It.IsAny<Dictionary<string, byte[]>>()), Times.Once);
        nav.Uri.Should().Contain($"/trip/{tripSlug}?currency=");
    }
}
